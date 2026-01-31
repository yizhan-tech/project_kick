using System.Collections.Generic;
using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;

public enum AgentState { Regular, Stalled, Armed, Restricted }

public class AgentController : Agent
{
    [Header("State Management")]
    [SerializeField] private AgentState _currentState = AgentState.Regular;
    public AgentState CurrentState => _currentState;

    [Header("Goalpost References")]
    [Tooltip("Target Goal - Left Post")] public Transform postTargetL;
    [Tooltip("Target Goal - Right Post")] public Transform postTargetR;
    [Tooltip("Own Goal - Left Post")] public Transform postOwnL;
    [Tooltip("Own Goal - Right Post")] public Transform postOwnR;
    
    [Header("Locomotion")]
    public Rigidbody playerRb;
    public float moveSpeed = 6f;
    public float turnSpeed = 250f;

    [Header("Stamina")]
    public float maxStamina = 100f;
    public float staminaDrainRate = 12f;   
    public float rotationStaminaCost = 60f;
    public float staminaRegenRate = 15f;
    public float sprintThreshold = 1.5f; 
    
    [Range(0, 100)] public float currentStamina;
    [Range(0f, 1f)] public float exhaustionPenalty = 0.5f; 
    public float recoveryThreshold = 20f; // New Hysteresis Threshold
    
    [SerializeField] private bool isExhausted = false; // Serialized for debugging

    [Header("Ball Interaction")]
    public Rigidbody ballRb;
    public Transform controlPoint;
    public float attractRange = 1.2f;
    public float attractForce = 120f;
    public float ballDamping = 5f;
    public float minForwardDot = 0.5f;
    public float lostControlCooldown = 0.5f; 
    
    private float attractDisabledUntil = 0f; 

    [Header("Kick Logic")]
    public float lowKick = 0.5f;
    public float midKick = 1.2f;
    public float highKick = 2.5f;

    [HideInInspector] public EnvController env;
    [HideInInspector] public bool isControlActive = false;
    [HideInInspector] public int CachedTeamId = -1;

    [Header("Cooldowns")]
    public float kickInternalCooldown = 1.0f; 
    private float nextKickAllowedTime = 0f;

    [Header("Debug Settings")]
    public bool showObservationDebug = false; // Check this box to start printing
    private float _nextDebugPrintTime = 0f;

    // --- INTERNAL PHYSICS VARIABLES ---
    private Quaternion lastRotation;
    private float currentAngularSpeed;
    private float _minYRotation = 0f;
    private float _maxYRotation = 360f;

    [HideInInspector] public bool IsBenched = false;

    //======================================
    // Object State & Initialization
    //======================================
    private BallController ballScript;
    public override void Initialize()
    {
        currentStamina = maxStamina;
        if (playerRb == null) playerRb = GetComponent<Rigidbody>();
        if (ballRb != null) ballScript = ballRb.GetComponent<BallController>();
    }

    public override void OnEpisodeBegin()
    {
        currentStamina = maxStamina;
        isExhausted = false; // Reset status
        attractDisabledUntil = 0f;
        playerRb.linearVelocity = Vector3.zero;
        playerRb.angularVelocity = Vector3.zero;
        
        lastRotation = transform.rotation;
        
        _currentState = AgentState.Regular;
        SetRotationLimits(0f, 360f);
    }

    private void FixedUpdate()
    {
        // 1. CALCULATE ANGULAR SPEED
        float deltaAngle = Quaternion.Angle(transform.rotation, lastRotation);
        currentAngularSpeed = deltaAngle / Time.fixedDeltaTime;
        lastRotation = transform.rotation;
        
        // 2. LOGIC
        HandleBallSnap();
        HandleBallAttraction(); 
        UpdateShields();

        // 3. STAMINA
        UpdateStamina();
    }

    private void HandleBallSnap()
    {
        if (_currentState == AgentState.Stalled || _currentState == AgentState.Armed)
        {
            ballRb.linearVelocity = Vector3.zero;
            ballRb.angularVelocity = Vector3.zero;

            float agentHalfWidth = 0.6f;
            float ballRadius = 0.6f;
            
            Vector3 spawnOffset = transform.forward * (agentHalfWidth + ballRadius);
            spawnOffset.y = ballRadius; 

            ballRb.position = transform.position + spawnOffset;
            ballRb.rotation = transform.rotation;
        }
    }

    //======================================
    // The Pilot (Input & Actions)
    //======================================   
    public override void OnActionReceived(ActionBuffers actions)
    {
        // 1. Define the Multiplier (Team A = 1, Team B = -1)
        float sideMultiplier = (CachedTeamId == 0) ? 1f : -1f;

        // 2. Pass the multiplier into movement logic
        // We use the multiplier to ensure 'Strafe Right' is consistent for both teams
        HandleMovement(actions.DiscreteActions[0], actions.DiscreteActions[1], sideMultiplier);
        
        int kickPower = actions.DiscreteActions[2];
        if (kickPower > 0) HandleKick(kickPower);
    }

    public override void Heuristic(in ActionBuffers actionsOut)
    {
        var d = actionsOut.DiscreteActions;
        
        if (!isControlActive)
        {
            d[0] = 0; d[1] = 0; d[2] = 0;
            return;
        }

        if (Input.GetKey(KeyCode.W)) d[0] = 1;
        else if (Input.GetKey(KeyCode.S)) d[0] = 2;
        else if (Input.GetKey(KeyCode.A)) d[0] = 3;
        else if (Input.GetKey(KeyCode.D)) d[0] = 4;

        if (Input.GetKey(KeyCode.Q)) d[1] = 1;
        else if (Input.GetKey(KeyCode.E)) d[1] = 2;

        if (Input.GetKey(KeyCode.J)) d[2] = 1;
        else if (Input.GetKey(KeyCode.K)) d[2] = 2;
        else if (Input.GetKey(KeyCode.L)) d[2] = 3;
    }

    //======================================
    // Locomotion (Physics)
    //====================================== 
    private void HandleMovement(int moveDir, int rotDir, float sideMultiplier)
    {
        switch (_currentState)
        {
            case AgentState.Stalled:
                playerRb.linearVelocity = Vector3.zero;
                return;

            case AgentState.Armed:
                playerRb.linearVelocity = Vector3.zero;
                ApplyRotationOnly(rotDir); 
                return;

            case AgentState.Regular:
            case AgentState.Restricted:
                ApplyFullMovement(moveDir, rotDir, sideMultiplier);
                break;
        }
    }

    public void SetRotationLimits(float min, float max)
    {
        _minYRotation = min;
        _maxYRotation = max;
    }

    private float CalculateRotationStep(int rotDir)
    {
        float effectiveTurnSpeed = isExhausted ? turnSpeed * exhaustionPenalty : turnSpeed;
        return (rotDir == 1 ? -1f : 1f) * effectiveTurnSpeed * Time.fixedDeltaTime;
    }

    private void ApplyRotationOnly(int rotDir)
    {
        if (rotDir == 0) return;
        
        // 1. Get the step using the helper
        float rotationAmount = CalculateRotationStep(rotDir);
        
        // 2. Apply strict limit logic
        float potentialRotation = transform.eulerAngles.y + rotationAmount;
        potentialRotation = Mathf.Repeat(potentialRotation, 360f);

        if (IsAngleInRange(potentialRotation, _minYRotation, _maxYRotation))
        {
            playerRb.MoveRotation(Quaternion.Euler(0, potentialRotation, 0));
        }
    }

    private void ApplyFullMovement(int moveDir, int rotDir, float sideMultiplier)
    {
        // 1. ROTATION LOCKING (The Drift Killer)
        if (rotDir != 0)
        {
            // Re-enable rotation only when actively turning
            playerRb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
            
            float rotationAmount = CalculateRotationStep(rotDir);
            playerRb.MoveRotation(playerRb.rotation * Quaternion.Euler(0, rotationAmount, 0));
        }
        else
        {
            // When NOT turning, freeze Y as well to stop the "decay" and "drift"
            playerRb.constraints = RigidbodyConstraints.FreezeRotationX | 
                                RigidbodyConstraints.FreezeRotationZ | 
                                RigidbodyConstraints.FreezeRotationY;
            
            playerRb.angularVelocity = Vector3.zero;
        }

        // 2. TRANSLATION (The Jitter Killer)
        Vector3 targetDir = Vector3.zero;
        if (moveDir == 1) targetDir = transform.forward;
        else if (moveDir == 2) targetDir = -transform.forward;
        else if (moveDir == 3) targetDir = -transform.right * sideMultiplier;
        else if (moveDir == 4) targetDir = transform.right * sideMultiplier;

        float effectiveMoveSpeed = isExhausted ? moveSpeed * exhaustionPenalty : moveSpeed;
        
        if (moveDir != 0)
        {
            Vector3 targetVel = targetDir * effectiveMoveSpeed;
            
            // FIX: Instead of snapping velocity with '=', we calculate the difference 
            // and apply it. This stops the "flipping numbers" caused by overwriting physics.
            Vector3 velocityChange = (targetVel - playerRb.linearVelocity);
            velocityChange.y = 0; // Protect gravity
            
            playerRb.AddForce(velocityChange, ForceMode.VelocityChange);
        }
        else
        {
            // Hard horizontal stop
            playerRb.linearVelocity = new Vector3(0, playerRb.linearVelocity.y, 0);
        }
    }

    private void UpdateStamina()
    {
        Vector3 horizontalVelocity = new Vector3(playerRb.linearVelocity.x, 0, playerRb.linearVelocity.z);
        float linearSpeed = horizontalVelocity.magnitude;
        float rotationThreshold = 10f; 

        // ADDITIVE CONSUMPTION
        float totalDrain = 0f;
        bool isExerting = false;

        if (linearSpeed > sprintThreshold)
        {
            totalDrain += staminaDrainRate;
            isExerting = true;
        }

        if (currentAngularSpeed > rotationThreshold)
        {
            totalDrain += rotationStaminaCost;
            isExerting = true;
        }

        if (isExerting)
        {
            currentStamina -= totalDrain * Time.fixedDeltaTime;
        }
        else
        {
            currentStamina += staminaRegenRate * Time.fixedDeltaTime;
        }

        currentStamina = Mathf.Clamp(currentStamina, 0, maxStamina);

        // --- HYSTERESIS STATE UPDATE ---
        if (currentStamina <= 0.1f)
        {
            isExhausted = true;
        }
        else if (currentStamina > recoveryThreshold)
        {
            isExhausted = false;
        }
    }

    //======================================
    // Ball Interaction (Skills)
    //====================================== 
    
    void HandleBallAttraction()
    {       
        if (Time.time < attractDisabledUntil) return;
        
        Vector3 toBall = ballRb.position - transform.position;
        
        if (toBall.magnitude > attractRange) return;
        if (Vector3.Dot(transform.forward, toBall.normalized) < minForwardDot) return;

        float sharedMultiplier = ballScript.GetForceMultiplier();

        Vector3 targetPos = controlPoint.position;
        Vector3 posError = targetPos - ballRb.position;
        float dist = posError.magnitude;
        if (dist < 1e-5f) return;

        float distanceWeight = Mathf.Clamp01(dist / attractRange);
        Vector3 velError = playerRb.linearVelocity - ballRb.linearVelocity;
        
        Vector3 accel = (attractForce * sharedMultiplier * distanceWeight) * posError + (ballDamping * distanceWeight) * velError;
        
        accel = Vector3.ClampMagnitude(accel, 100f);
        ballRb.AddForce(accel, ForceMode.Acceleration);
    }

    private void HandleKick(int powerLevel)
    {
        if (_currentState == AgentState.Stalled) return;
        if (Time.time < nextKickAllowedTime) return;
        if (Time.time < attractDisabledUntil) return;
        
        if (Vector3.Distance(controlPoint.position, ballRb.position) < attractRange)
        {
            float impulse = (powerLevel == 1) ? lowKick : (powerLevel == 2) ? midKick : highKick;

            if (powerLevel == 3) currentStamina = Mathf.Max(0, currentStamina - 20f);
            if (isExhausted) 
            {
                impulse *= 0.5f; 
            }

            nextKickAllowedTime = Time.time + kickInternalCooldown;
            attractDisabledUntil = Time.time + lostControlCooldown;

            if (env != null)
            {
                env.BroadcastKickCooldown(ballRb.position, attractRange, lostControlCooldown);
            }

            ballScript.RegisterKick(this);
            ballRb.AddForce(transform.forward * impulse, ForceMode.Impulse);
            ballRb.angularVelocity = Vector3.zero;

            if (_currentState == AgentState.Armed)
            {
                SetState(AgentState.Restricted);
            }
        }
    }

    public void ApplyExternalCooldown(float duration)
    {
        float newTime = Time.time + duration;
        if (newTime > attractDisabledUntil)
        {
            attractDisabledUntil = newTime;
        }
    }

    public void SetState(AgentState newState)
    {
        _currentState = newState;
        if (_currentState == AgentState.Stalled || _currentState == AgentState.Armed)
        {
            playerRb.linearVelocity = Vector3.zero;
            playerRb.angularVelocity = Vector3.zero;
        }
    }

    //======================================
    // Shields & Helpers
    //======================================
    private void ApplyExclusionZone(Vector3 center, float radius)
    {
        float dist = Vector3.Distance(transform.position, center);
        if (dist < radius && dist > 0.01f)
        {
            Vector3 pushDir = (transform.position - center).normalized;
            pushDir.y = 0; 

            Vector3 targetPos = center + (pushDir * radius);
            transform.position = new Vector3(targetPos.x, transform.position.y, targetPos.z);

            float dot = Vector3.Dot(playerRb.linearVelocity, pushDir);
            if (dot < 0) 
            {
                playerRb.linearVelocity -= pushDir * dot;
            }
        }
    }

    private void UpdateShields()
    {
        if (env.setPieceTaker != null && env.setPieceTaker != this)
        {
            if (env.setPieceTaker.CurrentState == AgentState.Stalled || 
                env.setPieceTaker.CurrentState == AgentState.Armed)
            {
                ApplyExclusionZone(env.setPieceTaker.transform.position, 5.0f);
            }
        }

        if (_currentState == AgentState.Restricted)
        {
            if (Time.time >= attractDisabledUntil)
            {
                ApplyExclusionZone(ballRb.position, 2.5f);
            }
        }
    }

    private bool IsAngleInRange(float angle, float min, float max)
    {
        angle = Mathf.Repeat(angle, 360f);
        min = Mathf.Repeat(min, 360f);
        max = Mathf.Repeat(max, 360f);

        if (min <= max) return angle >= min && angle <= max;
        else return angle >= min || angle <= max;
    }

    //=============================================================================
    // AI SENSES (OBSERVATIONS)
    //=============================================================================
    public override void CollectObservations(VectorSensor sensor)
    {
        List<float> currentObsDebug = showObservationDebug ? new List<float>() : null;

        float fieldWidth = 19f;
        float fieldLength = 34f;
        float maxSpeed = 15f;

        // --- THE MIRROR MULTIPLIER ---
        // Team 0 (A) multiplier = 1, Team 1 (B) multiplier = -1
        float sideMultiplier = (CachedTeamId == 0) ? 1f : -1f;

        // --- 1. POSSESSION STATE (+1 float) ---
        float possessionSignal = 0f;
        if (env.currentPhase == GamePhase.TeamAPossession)
            possessionSignal = (CachedTeamId == 0) ? 1f : -1f;
        else if (env.currentPhase == GamePhase.TeamBPossession)
            possessionSignal = (CachedTeamId == 1) ? 1f : -1f;

        AddObs(sensor, currentObsDebug, possessionSignal);

        // --- 1. SELF AWARENESS (7 values) ---
        float selfX = transform.localPosition.x / fieldWidth;
        float selfZ = (transform.localPosition.z * sideMultiplier) / fieldLength; // FLIPPED
        
        // For Rotation: Team B needs to add 180 degrees to their Y-rotation 
        // so their "forward" points toward the target goal.
        float visualYRotation = transform.localRotation.eulerAngles.y;
        if (CachedTeamId == 1) visualYRotation += 180f; 
        
        float rotRad = visualYRotation * Mathf.Deg2Rad;
        float sinRot = Mathf.Sin(rotRad);
        float cosRot = Mathf.Cos(rotRad);
        
        float selfVelX = playerRb.linearVelocity.x / maxSpeed;
        float selfVelZ = (playerRb.linearVelocity.z * sideMultiplier) / maxSpeed; // FLIPPED
        float stam = currentStamina / maxStamina;

        AddObs(sensor, currentObsDebug, selfX);
        AddObs(sensor, currentObsDebug, selfZ);
        AddObs(sensor, currentObsDebug, sinRot);
        AddObs(sensor, currentObsDebug, cosRot);
        AddObs(sensor, currentObsDebug, selfVelX);
        AddObs(sensor, currentObsDebug, selfVelZ);
        AddObs(sensor, currentObsDebug, stam);

        // --- 2. THE BALL (4 values) ---
        Vector3 toBall = ballRb.transform.localPosition - transform.localPosition;
        float ballX = toBall.x / (fieldWidth * 2);
        float ballZ = (toBall.z * sideMultiplier) / (fieldLength * 2); // FLIPPED
        
        float ballVelX = ballRb.linearVelocity.x / maxSpeed;
        float ballVelZ = (ballRb.linearVelocity.z * sideMultiplier) / maxSpeed; // FLIPPED

        AddObs(sensor, currentObsDebug, ballX);
        AddObs(sensor, currentObsDebug, ballZ);
        AddObs(sensor, currentObsDebug, ballVelX);
        AddObs(sensor, currentObsDebug, ballVelZ);

        // --- 3. THE GOALS (4 values) ---
        // With mirroring, Opponent Goal is ALWAYS at +34 and Own Goal is ALWAYS at -34
        float relativeZToOwnGoal = (-34f - (transform.localPosition.z * sideMultiplier)) / (fieldLength * 2);
        float relativeZToOppGoal = (34f - (transform.localPosition.z * sideMultiplier)) / (fieldLength * 2);
        
        // X relative to center (0)
        float relativeXToGoals = (0f - transform.localPosition.x) / (fieldWidth * 2);

        AddObs(sensor, currentObsDebug, relativeXToGoals);
        AddObs(sensor, currentObsDebug, relativeZToOwnGoal);
        AddObs(sensor, currentObsDebug, relativeXToGoals);
        AddObs(sensor, currentObsDebug, relativeZToOppGoal);

        Transform[] orderedPosts = { postTargetL, postTargetR, postOwnL, postOwnR };

        foreach (var post in orderedPosts)
        {
            if (post != null)
            {
                // Convert to Local relative to EnvRoot
                Vector3 localPostPos = env.transform.InverseTransformPoint(post.position);
                Vector3 toPost = localPostPos - transform.localPosition;

                // FIX: Divide by full field dimensions to ensure -1 to 1 range
                float postX = toPost.x / (fieldWidth * 2); 
                float postZ = (toPost.z * sideMultiplier) / (fieldLength * 2);

                AddObs(sensor, currentObsDebug, postX);
                AddObs(sensor, currentObsDebug, postZ);
            }
            else
            {
                AddObs(sensor, currentObsDebug, 0f);
                AddObs(sensor, currentObsDebug, 0f);
            }
        }

        // --- 4. OTHER PLAYERS (Relative & Sorted) ---
        bool isTeamA = (CachedTeamId == 0);
        
        // Pass the sideMultiplier through to the custom logic if needed, 
        // but here we just flip the results within the helper calls.
        // NOTE: We wrap the List in the correct order so Team A sees Team A as teammates.
        
        // Teammates (8 values)
        AddMirroredAgents(sensor, currentObsDebug, isTeamA ? env.teamA_Agents : env.teamB_Agents, true, fieldWidth, fieldLength, maxSpeed, sideMultiplier);
        
        // Opponents (12 values)
        AddMirroredAgents(sensor, currentObsDebug, isTeamA ? env.teamB_Agents : env.teamA_Agents, false, fieldWidth, fieldLength, maxSpeed, sideMultiplier);

        // --- PRINT DEBUG LOG ---
        if (showObservationDebug && Time.time >= _nextDebugPrintTime)
        {
            _nextDebugPrintTime = Time.time + 5f;
            PrintFullObservationLog(currentObsDebug);
        }
    }

    // --- HELPER 1: Add Single Value Wrapper ---
    private void AddObs(VectorSensor sensor, List<float> debugList, float val)
    {
        sensor.AddObservation(val);
        if (debugList != null) debugList.Add(val);
    }

    // --- HELPER 2: Sort and Add Agents ---
    private void AddMirroredAgents(VectorSensor sensor, List<float> debugList, List<AgentController> agents, bool viewingTeammates, float width, float length, float maxSpeed, float sideMultiplier)
    {
        List<AgentController> sortedList = new List<AgentController>(agents);
        if (viewingTeammates && sortedList.Contains(this)) sortedList.Remove(this);

        sortedList.Sort((a, b) => 
            Vector3.Distance(transform.position, a.transform.position)
            .CompareTo(Vector3.Distance(transform.position, b.transform.position))
        );

        int slotsToFill = viewingTeammates ? 2 : 3; 

        for (int i = 0; i < slotsToFill; i++)
        {
            if (i < sortedList.Count && !sortedList[i].IsBenched)
            {
                AgentController other = sortedList[i];
                Vector3 relativePos = other.transform.localPosition - transform.localPosition;

                AddObs(sensor, debugList, relativePos.x / (width * 2));
                AddObs(sensor, debugList, (relativePos.z * sideMultiplier) / (length * 2)); // FLIPPED
                AddObs(sensor, debugList, other.playerRb.linearVelocity.x / maxSpeed);
                AddObs(sensor, debugList, (other.playerRb.linearVelocity.z * sideMultiplier) / maxSpeed); // FLIPPED
            }
            else
            {
                AddObs(sensor, debugList, 0f);
                AddObs(sensor, debugList, 0f);
                AddObs(sensor, debugList, 0f);
                AddObs(sensor, debugList, 0f);
            }
        }
    }

    // --- HELPER 3: Print Formatted Log ---
    private void PrintFullObservationLog(List<float> obs)
    {
        if (obs == null || obs.Count < 36) return; // Note: Changed 35 to 36

        string log = $"<color=orange><b>[OBSERVATION DEBUG] {name}</b></color>\n";
        log += $"<b>Total Inputs:</b> {obs.Count}\n";
        
        // NEW LINE HERE
        log += $"<b>POSSESSION (1):</b> {obs[0]:F2} (1=Us, -1=Them, 0=Loose)\n";

        // Shift indices by +1 for the rest
        log += $"<b>SELF (7):</b> Pos[{obs[1]:F2}, {obs[2]:F2}] Rot[{obs[3]:F2}, {obs[4]:F2}] Vel[{obs[5]:F2}, {obs[6]:F2}] Stam[{obs[7]:F2}]\n";
        
        log += $"<b>BALL (4):</b> RelPos[{obs[8]:F2}, {obs[9]:F2}] Vel[{obs[10]:F2}, {obs[11]:F2}]\n";
        
        log += $"<b>GOALS (4):</b> Own[{obs[12]:F2}, {obs[13]:F2}] Opp[{obs[14]:F2}, {obs[15]:F2}]\n";

        log += $"<b>GOALPOSTS (8):</b> P1[{obs[16]:F2}, {obs[17]:F2}] P2[{obs[18]:F2}, {obs[19]:F2}] P3[{obs[20]:F2}, {obs[21]:F2}] P4[{obs[22]:F2}, {obs[23]:F2}]\n";
        
        // ... (Update remaining indices by adding 1 to everything) ...
        // Teammates start at 16
        // Opponents start at 24
        
        Debug.Log(log);
    }
}