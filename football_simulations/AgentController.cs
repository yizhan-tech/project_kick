using System.Collections.Generic;
using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;

public enum AgentState { Regular, Stalled, Armed, Restricted }

[RequireComponent(typeof(Rigidbody))]
public class AgentController : Agent
{
    // =================================================================================================================
    // 1. INSPECTOR SETTINGS
    // =================================================================================================================
    
    [Header("State Management")]
    [SerializeField] private AgentState _currentState = AgentState.Regular;
    public AgentState CurrentState => _currentState;

    [Header("Goalpost References")]
    public Transform postTargetL;
    public Transform postTargetR;
    public Transform postOwnL;
    public Transform postOwnR;
    
    [Header("Locomotion")]
    public float moveSpeed = 6f;
    public float turnSpeed = 250f;

    [Header("Stamina System")]
    public float maxStamina = 100f;
    public float staminaDrainRate = 12f;   
    public float rotationStaminaCost = 60f;
    public float staminaRegenRate = 15f;
    public float sprintThreshold = 1.5f; 
    
    [Range(0, 100)] public float currentStamina;
    [Range(0f, 1f)] public float exhaustionPenalty = 0.5f; 
    public float recoveryThreshold = 20f; // Hysteresis Threshold
    
    [SerializeField] private bool isExhausted = false; 

    [Header("Ball Interaction")]
    public Rigidbody ballRb;
    public Transform controlPoint;
    public float attractRange = 2.0f;
    public float attractForce = 120f;
    public float ballDamping = 5f;
    public float minForwardDot = 0.5f;
    public float lostControlCooldown = 0.5f; 

    [Header("Kick Logic")]
    public float lowKick = 0.5f;
    public float midKick = 1.2f;
    public float highKick = 2.5f;
    public float kickInternalCooldown = 1.0f; 

    [Header("Debug")]
    public bool showObservationDebug = false;

    // =================================================================================================================
    // 2. INTERNAL STATE & CACHE
    // =================================================================================================================
    
    [HideInInspector] public EnvController env;
    [HideInInspector] public Rigidbody playerRb;
    [HideInInspector] public bool isControlActive = false;
    [HideInInspector] public int CachedTeamId = -1;
    [HideInInspector] public bool IsBenched = false;

    // Physics & Timers
    private BallController ballScript;
    private Quaternion lastRotation;
    private float currentAngularSpeed;
    private float _minYRotation = 0f;
    private float _maxYRotation = 360f;
    private float attractDisabledUntil = 0f; 
    private float nextKickAllowedTime = 0f;
    private float _nextDebugPrintTime = 0f;

    // =================================================================================================================
    // 3. INITIALIZATION & LIFECYCLE
    // =================================================================================================================

    public override void Initialize()
    {
        currentStamina = maxStamina;
        playerRb = GetComponent<Rigidbody>();
        if (ballRb != null) ballScript = ballRb.GetComponent<BallController>();
    }

    public override void OnEpisodeBegin()
    {
        currentStamina = maxStamina;
        isExhausted = false; 
        attractDisabledUntil = 0f;
        
        playerRb.linearVelocity = Vector3.zero;
        playerRb.angularVelocity = Vector3.zero;
        lastRotation = transform.rotation;
        
        _currentState = AgentState.Regular;
        SetRotationLimits(0f, 360f);
    }

    private void FixedUpdate()
    {
        // 1. Calculate Angular Speed for Stamina
        float deltaAngle = Quaternion.Angle(transform.rotation, lastRotation);
        currentAngularSpeed = deltaAngle / Time.fixedDeltaTime;
        lastRotation = transform.rotation;
        
        // 2. Core Logic
        HandleBallSnap();
        HandleBallAttraction(); 
        UpdateShields();
        UpdateStamina();
    }

    private void HandleBallSnap()
    {
        // If Stalled or Armed, lock the ball to the agent's front for set pieces
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

    // =================================================================================================================
    // 4. ML-AGENTS: ACTIONS & INPUT
    // =================================================================================================================
    
    public override void OnActionReceived(ActionBuffers actions)
    {
        // Define Side Multiplier (Team A = 1, Team B = -1)
        float sideMultiplier = (CachedTeamId == 0) ? 1f : -1f;

        // Locomotion (Discrete 0 & 1)
        HandleMovement(actions.DiscreteActions[0], actions.DiscreteActions[1], sideMultiplier);
        
        // Kicking (Discrete 2)
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

        // WASD Movement
        if (Input.GetKey(KeyCode.W)) d[0] = 1;
        else if (Input.GetKey(KeyCode.S)) d[0] = 2;
        else if (Input.GetKey(KeyCode.A)) d[0] = 3;
        else if (Input.GetKey(KeyCode.D)) d[0] = 4;

        // Rotation
        if (Input.GetKey(KeyCode.Q)) d[1] = 1;
        else if (Input.GetKey(KeyCode.E)) d[1] = 2;

        // Kicking
        if (Input.GetKey(KeyCode.J)) d[2] = 1; // Low
        else if (Input.GetKey(KeyCode.K)) d[2] = 2; // Mid
        else if (Input.GetKey(KeyCode.L)) d[2] = 3; // High
    }

    // =================================================================================================================
    // 5. LOCOMOTION PHYSICS
    // =================================================================================================================
    
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
        
        float rotationAmount = CalculateRotationStep(rotDir);
        float potentialRotation = transform.eulerAngles.y + rotationAmount;
        potentialRotation = Mathf.Repeat(potentialRotation, 360f);

        if (IsAngleInRange(potentialRotation, _minYRotation, _maxYRotation))
        {
            playerRb.MoveRotation(Quaternion.Euler(0, potentialRotation, 0));
        }
    }

    private void ApplyFullMovement(int moveDir, int rotDir, float sideMultiplier)
    {
        // A. ROTATION
        if (rotDir != 0)
        {
            playerRb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
            float rotationAmount = CalculateRotationStep(rotDir);
            playerRb.MoveRotation(playerRb.rotation * Quaternion.Euler(0, rotationAmount, 0));
        }
        else
        {
            // Lock Y to stop drift when not turning
            playerRb.constraints = RigidbodyConstraints.FreezeRotationX | 
                                RigidbodyConstraints.FreezeRotationZ | 
                                RigidbodyConstraints.FreezeRotationY;
            playerRb.angularVelocity = Vector3.zero;
        }

        // B. TRANSLATION
        Vector3 targetDir = Vector3.zero;
        if (moveDir == 1) targetDir = transform.forward;
        else if (moveDir == 2) targetDir = -transform.forward;
        else if (moveDir == 3) targetDir = -transform.right * sideMultiplier;
        else if (moveDir == 4) targetDir = transform.right * sideMultiplier;

        float effectiveMoveSpeed = isExhausted ? moveSpeed * exhaustionPenalty : moveSpeed;
        
        if (moveDir != 0)
        {
            Vector3 targetVel = targetDir * effectiveMoveSpeed;
            Vector3 velocityChange = (targetVel - playerRb.linearVelocity);
            velocityChange.y = 0; // Preserve gravity
            
            playerRb.AddForce(velocityChange, ForceMode.VelocityChange);
        }
        else
        {
            // Hard Stop
            playerRb.linearVelocity = new Vector3(0, playerRb.linearVelocity.y, 0);
        }
    }

    // =================================================================================================================
    // 6. STAMINA SYSTEM
    // =================================================================================================================

    private void UpdateStamina()
    {
        Vector3 horizontalVelocity = new Vector3(playerRb.linearVelocity.x, 0, playerRb.linearVelocity.z);
        float linearSpeed = horizontalVelocity.magnitude;
        float rotationThreshold = 10f; 

        float totalDrain = 0f;
        bool isExerting = false;

        // Drain from running
        if (linearSpeed > sprintThreshold)
        {
            totalDrain += staminaDrainRate;
            isExerting = true;
        }

        // Drain from turning
        if (currentAngularSpeed > rotationThreshold)
        {
            totalDrain += rotationStaminaCost;
            isExerting = true;
        }

        // Apply Change
        if (isExerting)
        {
            currentStamina -= totalDrain * Time.fixedDeltaTime;
        }
        else
        {
            currentStamina += staminaRegenRate * Time.fixedDeltaTime;
        }

        currentStamina = Mathf.Clamp(currentStamina, 0, maxStamina);

        // Hysteresis Check
        if (currentStamina <= 0.1f) isExhausted = true;
        else if (currentStamina > recoveryThreshold) isExhausted = false;
    }

    // =================================================================================================================
    // 7. BALL INTERACTION (SKILLS)
    // =================================================================================================================
    
    void HandleBallAttraction()
    {       
        if (Time.time < attractDisabledUntil) return;
        
        Vector3 toBall = ballRb.position - transform.position;
        if (toBall.magnitude > attractRange) return;
        
        // Only attract if ball is generally in front
        if (Vector3.Dot(transform.forward, toBall.normalized) < minForwardDot) return;

        float sharedMultiplier = ballScript.GetForceMultiplier();

        Vector3 targetPos = controlPoint.position;
        Vector3 posError = targetPos - ballRb.position;
        float dist = posError.magnitude;
        if (dist < 1e-5f) return;

        // Physics: Damped Spring force to pull ball to feet
        float distanceWeight = Mathf.Clamp01(dist / attractRange);
        Vector3 velError = playerRb.linearVelocity - ballRb.linearVelocity;
        
        Vector3 accel = (attractForce * sharedMultiplier * distanceWeight) * posError + (ballDamping * distanceWeight) * velError;
        accel = Vector3.ClampMagnitude(accel, 100f);
        
        ballRb.AddForce(accel, ForceMode.Acceleration);
    }

    private void HandleKick(int powerLevel)
    {
        // Pre-checks
        if (_currentState == AgentState.Stalled) return;
        if (Time.time < nextKickAllowedTime) return;
        if (Time.time < attractDisabledUntil) return;
        
        // Distance check
        if (Vector3.Distance(controlPoint.position, ballRb.position) < attractRange)
        {
            float impulse = (powerLevel == 1) ? lowKick : (powerLevel == 2) ? midKick : highKick;

            // Stamina cost for high kick
            if (powerLevel == 3) currentStamina = Mathf.Max(0, currentStamina - 20f);
            
            // Exhaustion penalty
            if (isExhausted) impulse *= 0.5f; 

            // Set Cooldowns
            nextKickAllowedTime = Time.time + kickInternalCooldown;
            attractDisabledUntil = Time.time + lostControlCooldown;

            // Broadcast
            if (env != null)
            {
                env.BroadcastKickCooldown(ballRb.position, attractRange, lostControlCooldown);
            }

            // Apply Kick
            ballScript.RegisterKick(this);
            ballRb.AddForce(transform.forward * impulse, ForceMode.Impulse);
            ballRb.angularVelocity = Vector3.zero; // Clean hit

            // Release Armed State
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

    // =================================================================================================================
    // 8. SHIELDS & HELPERS
    // =================================================================================================================

    private void ApplyExclusionZone(Vector3 center, float radius)
    {
        float dist = Vector3.Distance(transform.position, center);
        if (dist < radius && dist > 0.01f)
        {
            Vector3 pushDir = (transform.position - center).normalized;
            pushDir.y = 0; 

            Vector3 targetPos = center + (pushDir * radius);
            transform.position = new Vector3(targetPos.x, transform.position.y, targetPos.z);

            // Kill velocity into the zone
            float dot = Vector3.Dot(playerRb.linearVelocity, pushDir);
            if (dot < 0) 
            {
                playerRb.linearVelocity -= pushDir * dot;
            }
        }
    }

    private void UpdateShields()
    {
        // 1. Respect Set Piece Taker (5m radius)
        if (env.setPieceTaker != null && env.setPieceTaker != this)
        {
            if (env.setPieceTaker.CurrentState == AgentState.Stalled || 
                env.setPieceTaker.CurrentState == AgentState.Armed)
            {
                ApplyExclusionZone(env.setPieceTaker.transform.position, 5.0f);
            }
        }

        // 2. Restricted players (defenders) must stay 2.5m away from ball on free kicks
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

    // =================================================================================================================
    // 9. ML-AGENTS: OBSERVATIONS (44 Inputs)
    // =================================================================================================================
    
    public override void CollectObservations(VectorSensor sensor)
    {
        List<float> currentObsDebug = showObservationDebug ? new List<float>() : null;

        float fieldWidth = 19f;
        float fieldLength = 34f;
        float maxSpeed = 15f;
        float sideMultiplier = (CachedTeamId == 0) ? 1f : -1f;

        // A. POSSESSION STATE (1)
        float possessionSignal = 0f;
        if (env.currentPhase == GamePhase.TeamAPossession) possessionSignal = (CachedTeamId == 0) ? 1f : -1f;
        else if (env.currentPhase == GamePhase.TeamBPossession) possessionSignal = (CachedTeamId == 1) ? 1f : -1f;
        AddObs(sensor, currentObsDebug, possessionSignal);

        // B. SELF AWARENESS (7)
        float selfX = transform.localPosition.x / fieldWidth;
        float selfZ = (transform.localPosition.z * sideMultiplier) / fieldLength; 
        
        // Correct rotation for Team B (180 deg offset)
        float visualYRotation = transform.localRotation.eulerAngles.y;
        if (CachedTeamId == 1) visualYRotation += 180f; 
        
        float rotRad = visualYRotation * Mathf.Deg2Rad;
        
        AddObs(sensor, currentObsDebug, selfX);
        AddObs(sensor, currentObsDebug, selfZ);
        AddObs(sensor, currentObsDebug, Mathf.Sin(rotRad));
        AddObs(sensor, currentObsDebug, Mathf.Cos(rotRad));
        AddObs(sensor, currentObsDebug, playerRb.linearVelocity.x / maxSpeed);
        AddObs(sensor, currentObsDebug, (playerRb.linearVelocity.z * sideMultiplier) / maxSpeed);
        AddObs(sensor, currentObsDebug, currentStamina / maxStamina);

        // C. THE BALL (4)
        Vector3 toBall = ballRb.transform.localPosition - transform.localPosition;
        AddObs(sensor, currentObsDebug, toBall.x / (fieldWidth * 2));
        AddObs(sensor, currentObsDebug, (toBall.z * sideMultiplier) / (fieldLength * 2));
        AddObs(sensor, currentObsDebug, ballRb.linearVelocity.x / maxSpeed);
        AddObs(sensor, currentObsDebug, (ballRb.linearVelocity.z * sideMultiplier) / maxSpeed);

        // D. GOALS & POSTS (12)
        // 4 Relative Coordinates
        float relativeZToOwnGoal = (-34f - (transform.localPosition.z * sideMultiplier)) / (fieldLength * 2);
        float relativeZToOppGoal = (34f - (transform.localPosition.z * sideMultiplier)) / (fieldLength * 2);
        float relativeXToGoals = (0f - transform.localPosition.x) / (fieldWidth * 2);

        AddObs(sensor, currentObsDebug, relativeXToGoals);
        AddObs(sensor, currentObsDebug, relativeZToOwnGoal);
        AddObs(sensor, currentObsDebug, relativeXToGoals);
        AddObs(sensor, currentObsDebug, relativeZToOppGoal);

        // 8 Post Coordinates
        Transform[] orderedPosts = { postTargetL, postTargetR, postOwnL, postOwnR };
        foreach (var post in orderedPosts)
        {
            if (post != null)
            {
                Vector3 localPostPos = env.transform.InverseTransformPoint(post.position);
                Vector3 toPost = localPostPos - transform.localPosition;
                AddObs(sensor, currentObsDebug, toPost.x / (fieldWidth * 2));
                AddObs(sensor, currentObsDebug, (toPost.z * sideMultiplier) / (fieldLength * 2));
            }
            else
            {
                AddObs(sensor, currentObsDebug, 0f);
                AddObs(sensor, currentObsDebug, 0f);
            }
        }

        // E. OTHER PLAYERS (20)
        bool isTeamA = (CachedTeamId == 0);
        
        // Teammates (8 inputs: 2 agents * 4 values)
        AddMirroredAgents(sensor, currentObsDebug, isTeamA ? env.teamA_Agents : env.teamB_Agents, true, fieldWidth, fieldLength, maxSpeed, sideMultiplier);
        
        // Opponents (12 inputs: 3 agents * 4 values)
        AddMirroredAgents(sensor, currentObsDebug, isTeamA ? env.teamB_Agents : env.teamA_Agents, false, fieldWidth, fieldLength, maxSpeed, sideMultiplier);

        // DEBUG PRINT
        if (showObservationDebug && Time.time >= _nextDebugPrintTime)
        {
            _nextDebugPrintTime = Time.time + 5f;
            PrintFullObservationLog(currentObsDebug);
        }
    }

    // --- HELPER METHODS FOR OBSERVATIONS ---

    private void AddObs(VectorSensor sensor, List<float> debugList, float val)
    {
        sensor.AddObservation(val);
        if (debugList != null) debugList.Add(val);
    }

    private void AddMirroredAgents(VectorSensor sensor, List<float> debugList, List<AgentController> agents, bool viewingTeammates, float width, float length, float maxSpeed, float sideMultiplier)
    {
        // 1. Filter and Sort (Clone list to avoid modifying original)
        List<AgentController> sortedList = new List<AgentController>(agents);
        if (viewingTeammates && sortedList.Contains(this)) sortedList.Remove(this);

        sortedList.Sort((a, b) => 
            Vector3.Distance(transform.position, a.transform.position)
            .CompareTo(Vector3.Distance(transform.position, b.transform.position))
        );

        // 2. Add Data
        int slotsToFill = viewingTeammates ? 2 : 3; 

        for (int i = 0; i < slotsToFill; i++)
        {
            if (i < sortedList.Count && !sortedList[i].IsBenched)
            {
                AgentController other = sortedList[i];
                Vector3 relativePos = other.transform.localPosition - transform.localPosition;

                AddObs(sensor, debugList, relativePos.x / (width * 2));
                AddObs(sensor, debugList, (relativePos.z * sideMultiplier) / (length * 2)); 
                AddObs(sensor, debugList, other.playerRb.linearVelocity.x / maxSpeed);
                AddObs(sensor, debugList, (other.playerRb.linearVelocity.z * sideMultiplier) / maxSpeed); 
            }
            else
            {
                // Pad with zeros if fewer agents than slots
                AddObs(sensor, debugList, 0f);
                AddObs(sensor, debugList, 0f);
                AddObs(sensor, debugList, 0f);
                AddObs(sensor, debugList, 0f);
            }
        }
    }

    private void PrintFullObservationLog(List<float> obs)
    {
        if (obs == null || obs.Count < 36) return;

        string log = $"<color=orange><b>[OBSERVATION DEBUG] {name}</b></color>\n";
        log += $"<b>Total Inputs:</b> {obs.Count}\n";
        
        log += $"<b>POSSESSION (1):</b> {obs[0]:F2} (1=Us, -1=Them, 0=Loose)\n";
        log += $"<b>SELF (7):</b> Pos[{obs[1]:F2}, {obs[2]:F2}] Rot[{obs[3]:F2}, {obs[4]:F2}] Vel[{obs[5]:F2}, {obs[6]:F2}] Stam[{obs[7]:F2}]\n";
        log += $"<b>BALL (4):</b> RelPos[{obs[8]:F2}, {obs[9]:F2}] Vel[{obs[10]:F2}, {obs[11]:F2}]\n";
        log += $"<b>GOALS (4):</b> Own[{obs[12]:F2}, {obs[13]:F2}] Opp[{obs[14]:F2}, {obs[15]:F2}]\n";
        log += $"<b>POSTS (8):</b> P1[{obs[16]:F2}, {obs[17]:F2}] P2[{obs[18]:F2}, {obs[19]:F2}] P3[{obs[20]:F2}, {obs[21]:F2}] P4[{obs[22]:F2}, {obs[23]:F2}]\n";
        log += $"<b>TEAMMATES (8):</b> T1[{obs[24]:F2}..] T2[{obs[28]:F2}..]\n";
        log += $"<b>OPPONENTS (12):</b> O1[{obs[32]:F2}..] O2[{obs[36]:F2}..] O3[{obs[40]:F2}..]\n";
        
        Debug.Log(log);
    }
}