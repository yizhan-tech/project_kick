using System.Collections.Generic;
using UnityEngine;
using Unity.MLAgents;

public enum GamePhase { Loose, TeamAPossession, TeamBPossession, Contested }
public enum PlayType { OpenPlay, GoalKick, Corner, ThrowIn }

public class EnvController : MonoBehaviour
{
    #region Configuration & Settings
    [Header("Curriculum Settings")]
    public List<Scenario> scenarios; 
    public int defaultScenarioIndex = 0; 

    [Header("References")]
    public BallController ballController;
    public List<AgentController> teamA_Agents; 
    public List<AgentController> teamB_Agents;

    [Header("Match Settings")]
    public int MaxEnvironmentSteps = 5000; 
    private int _currentEnvironmentStep = 0;

    [Header("Play State")]
    public PlayType currentPlayType = PlayType.OpenPlay;
    public GamePhase currentPhase = GamePhase.Loose;

    [Header("Set Piece Logic")]
    public AgentController setPieceTaker;
    public float stallTimer = 0f;
    public float simulatedRunSpeed = 6f; 

    [Header("Debug Settings")]
    public bool muteLog = false;
    #endregion

    #region Internal State
    // Registry
    private List<AgentController> masterAgentList = new List<AgentController>();
    private int currentControlIndex = 0;

    // ML-Agents Groups
    private SimpleMultiAgentGroup m_TeamAGroup;
    private SimpleMultiAgentGroup m_TeamBGroup;
    private float _cumulatedRewardA = 0f;
    private float _cumulatedRewardB = 0f;

    // Scenario Memory
    private bool _isFirstSpawn = true; 
    private Vector3 _lastValidBallPos = new Vector3(0, 0.61f, 0);
    private Dictionary<AgentController, Vector3> _lastValidAgentPositions = new Dictionary<AgentController, Vector3>();
    private Dictionary<AgentController, Vector3> _lastValidAgentRotations = new Dictionary<AgentController, Vector3>();

    // Instinct / Proximity Tracking
    private Dictionary<AgentController, float> _lastAgentToBallDists = new Dictionary<AgentController, float>();
    private float _lastBallToGoalDist = 999f;
    private string lastPhaseSignature = "";

    // Hex Colors for Logs
    private string colorTeamA = "#FF4C4C"; 
    private string colorTeamB = "#4C91FF"; 
    private string colorSystem = "#FFFF00"; 
    private string colorPhase = "#00FF00"; 
    private string colorRules = "#FFA500"; 
    
    #endregion

    #region Initialization
    void Awake()
    {
        // Initialize Groups (Empty)
        m_TeamAGroup = new SimpleMultiAgentGroup();
        m_TeamBGroup = new SimpleMultiAgentGroup();

        // Just populate the master list and set references
        // DO NOT call m_TeamAGroup.RegisterAgent() here!
        foreach (var agent in teamA_Agents) RegisterAgentReferences(agent, 0);
        foreach (var agent in teamB_Agents) RegisterAgentReferences(agent, 1);
        
        UpdateControlPermissions();
        foreach (var agent in masterAgentList) MoveToBench(agent); 
    }

    void RegisterAgentReferences(AgentController agent, int teamId)
    {
        if (agent != null)
        {
            agent.CachedTeamId = teamId;
            agent.env = this;
            var bp = agent.GetComponent<Unity.MLAgents.Policies.BehaviorParameters>();
            if (bp != null) bp.TeamId = teamId;
            masterAgentList.Add(agent);
        }
    }

    void Start()
    {
        StartCoroutine(InitialResetBuffer());
    }

    private System.Collections.IEnumerator InitialResetBuffer()
    {
        // Wait two frames to ensure academy/curriculum data is ready
        yield return new WaitForEndOfFrame();
        yield return new WaitForEndOfFrame();
        
        ResetSceneForGoal();
        LogEvent("<color=#FFFF00>[SYSTEM]</color> Environment Initialized via Scenario.");
    }
    #endregion

    #region Game Loop
    void Update()
    {
        // Debug: Swap manual control between agents
        if (Input.GetKeyDown(KeyCode.H)) SwapControl();
    }

    void FixedUpdate()
    {
        if (stallTimer > 0) 
        {
            stallTimer -= Time.fixedDeltaTime;
        }
        else
        {
            // Set Piece Logic: The "Whistle"
            if (setPieceTaker != null && setPieceTaker.CurrentState == AgentState.Stalled)
            {
                // Transition from Stalled (frozen) to Armed (can now kick)
                setPieceTaker.SetState(AgentState.Armed);
                LogEvent($"<color={colorRules}>[RULES]</color> Play Resumed. Taker is Armed.");
            }
        }

        _currentEnvironmentStep++;

        // 1. Apply Continuous "Kindergarten" Instinct Rewards (Movement/Pushing)

        ApplyColdStartProximityRewards();
        
        // 2. Max Step Timeout Check
        if (_currentEnvironmentStep >= MaxEnvironmentSteps)
        {
            if (_currentEnvironmentStep > 0) 
            {
                m_TeamAGroup.EndGroupEpisode(); 
                m_TeamBGroup.EndGroupEpisode();
                LogEpisodeSummary();
                ResetSceneForGoal();
            }
        }
    }
    #endregion

    #region Reward System (Instincts)
    private void AddRewardA(float amount) 
    { 
        m_TeamAGroup.AddGroupReward(amount); 
        _cumulatedRewardA += amount; 
        // Debug.Log($"Group A Reward Added: {amount}");
    }

    private void AddRewardB(float amount) 
    { 
        m_TeamBGroup.AddGroupReward(amount); 
        _cumulatedRewardB += amount; 
        // Debug.Log($"Group B Reward Added: {amount}");
    }

    private void ApplyColdStartProximityRewards()
    {
        int activePlayers = GetActiveAgentCount();

        // SCENARIO 3: Team Play (2v1, 2v2, etc.)
        // If more than 2 players, disable all dense rewards. 
        // They must rely on the Sparse Goal Reward (POCA).
        if (activePlayers > 2) return;

        // 1. Calculate Goal Positions
        Vector3 goalPosA = new Vector3(0, 0, 34.5f); // The goal Team A wants to shoot at
        Vector3 goalPosB = new Vector3(0, 0, -34.5f); // The goal Team B wants to shoot at

        // 2. Calculate Distances
        float currentBallToGoalA = Vector3.Distance(ballController.transform.localPosition, goalPosA);
        float currentBallToGoalB = Vector3.Distance(ballController.transform.localPosition, goalPosB);

        foreach (var agent in masterAgentList)
        {
            if (agent.IsBenched || !agent.gameObject.activeInHierarchy) continue;

            // Safety Initialization
            if (!_lastAgentToBallDists.ContainsKey(agent)) 
                _lastAgentToBallDists[agent] = Vector3.Distance(agent.transform.localPosition, ballController.transform.localPosition);

            float currentAgentToBallDist = Vector3.Distance(agent.transform.localPosition, ballController.transform.localPosition);
            float currentBallToTarget = (agent.CachedTeamId == 0) ? currentBallToGoalA : currentBallToGoalB;

            // ==============================================================================
            // INSTINCT A: Run to Ball (The "Magnet")
            // RULE: Only apply this in 1v0 (activePlayers == 1). 
            // In 1v1, we want them to learn defense/positioning, not just chasing.
            // ==============================================================================
            if (activePlayers == 1)
            {
                float moveDelta = _lastAgentToBallDists[agent] - currentAgentToBallDist;
                if (moveDelta > 0)
                {
                    // Tiny reward to encourage initial contact
                    float reward = moveDelta * 0.001f; 
                    if (agent.CachedTeamId == 0) AddRewardA(reward); 
                    else AddRewardB(reward);
                }
            }

            // ==============================================================================
            // INSTINCT B: Push Ball to Goal (The "Objective")
            // RULE: Apply in 1v0 AND 1v1.
            // Constraint: Agent must be very close (1.5m) to "own" this reward.
            // ==============================================================================
            if (currentAgentToBallDist < 1.5f)
            {
                // We use the agent's SPECIFIC target goal delta, not a global shared one.
                // This prevents Team B from getting rewards when Team A dribbles to their goal.
                // We need to track the ball's previous distance relative to the specific goal.
                
                // Note: Using a simplified calculation here for "Frame-to-Frame" progress
                // Since _lastBallToGoalDist is shared, we approximate the delta using the shared variable
                // but strictly gating it by the agent's proximity to the ball.
                
                float globalLast = _lastBallToGoalDist; // Using your existing shared var
                float globalCurrent = Mathf.Min(currentBallToGoalA, currentBallToGoalB);
                
                float pushDelta = globalLast - globalCurrent;

                // Only give reward if the ball actually moved closer to A Goal
                if (pushDelta > 0)
                {
                    // Scale this small so it never exceeds +1.0 over an episode
                    // 70 meters * 0.002 = 0.14 total potential reward. Safe.
                    float pushReward = pushDelta * 0.002f;
                    
                    if (agent.CachedTeamId == 0) AddRewardA(pushReward); 
                    else AddRewardB(pushReward);
                }
            }

            _lastAgentToBallDists[agent] = currentAgentToBallDist;
        }

        // Update shared ball state
        _lastBallToGoalDist = Mathf.Min(currentBallToGoalA, currentBallToGoalB);
    }
    #endregion

    #region Referee & Rules
    public void ResolveGoal(int scoringTeam, AgentController scorer, string action)
    {
        if (_currentEnvironmentStep <= 0) return;

        LogEvent($"<color=cyan>[GOAL TRIGGER]</color> Team {scoringTeam} scored!");

        // 1. Calculate Efficiency Reward (Faster goal = Higher reward)
        float efficiencyReward = Mathf.Clamp(1.0f - ((float)_currentEnvironmentStep / MaxEnvironmentSteps), 0.1f, 1.0f);

        // 2. Assign Group Rewards (Zero-Sum)
        if (scoringTeam == 0) {
            AddRewardA(efficiencyReward); 
            AddRewardB(-1.0f);            
        } else {
            AddRewardB(efficiencyReward);
            AddRewardA(-1.0f);
        }

        // 3. Individual POCA Bonus (Credit Assignment)
        if (scorer != null)
        {
            scorer.AddReward(scoringTeam == scorer.CachedTeamId ? 0.1f : -0.1f);
        }

        // 4. Handshake & Reset
        m_TeamAGroup.EndGroupEpisode();
        m_TeamBGroup.EndGroupEpisode();

        LogEpisodeSummary();
        ResetSceneForGoal();
    }

    public void ResolveOutOfBounds(AgentController responsibleAgent, int teamId, string action, Vector3 oobLocation)
    {
        // Convert to Local Space
        Vector3 localOOB = transform.InverseTransformPoint(oobLocation);

        // If not enough agents for a full match, just reset
        if (GetActiveAgentCount() < 4)
        {
            m_TeamAGroup.EndGroupEpisode();
            m_TeamBGroup.EndGroupEpisode();
            LogEpisodeSummary();
            ResetSceneForGoal();
            return; 
        }

        // Determine Set Piece Type
        currentPlayType = DetermineRestartType(teamId, localOOB);
        int beneficiaryId = (teamId == 0) ? 1 : 0;

        // Find Taker and Calculate Stall Timer
        setPieceTaker = GetClosestPlayerOnTeam(beneficiaryId, oobLocation); 
        
        if (setPieceTaker != null) {
            float distance = Vector3.Distance(setPieceTaker.transform.position, oobLocation);
            stallTimer = distance / simulatedRunSpeed;
        }

        LogEvent($"<color={colorRules}>[OOB]</color> Restart: {currentPlayType}");
        
        ResetSceneForSetPiece(localOOB);
    }

    public void ResetSceneForSetPiece(Vector3 oobImpact)
    {
        // 1. Calculate Restart Location
        Vector3 rawRestartPos = CalculateBaseRestartLocation(oobImpact);

        // 2. Position Ball & Taker
        Vector3 bufferedBallPos = PositionBallAndTaker(rawRestartPos);

        if (setPieceTaker != null)
        {
            // 3. Apply Rules (Rotation Limits & Magnet)
            ApplySetPieceRotationLimits(rawRestartPos);
            EnforceClearanceZone(setPieceTaker.transform.position, 5.0f);
        }
    }

    // --- Referee Helpers ---

    private PlayType DetermineRestartType(int lastTouchTeam, Vector3 pos)
    {
        float fieldLengthHalf = 34f;
        float fieldWidthHalf = 19f;

        if (Mathf.Abs(pos.x) >= fieldWidthHalf) return PlayType.ThrowIn;

        if (Mathf.Abs(pos.z) >= fieldLengthHalf)
        {
            if (pos.z > 0) return (lastTouchTeam == 0) ? PlayType.GoalKick : PlayType.Corner;
            else           return (lastTouchTeam == 1) ? PlayType.GoalKick : PlayType.Corner;
        }
        return PlayType.OpenPlay;
    }

    private Vector3 CalculateBaseRestartLocation(Vector3 impactPos)
    {
        if (currentPlayType == PlayType.GoalKick)
        {
            float goalZ = (impactPos.z > 0) ? 28.0f : -28.0f;
            return new Vector3(0f, impactPos.y, goalZ);
        }
        else if (currentPlayType == PlayType.Corner)
        {
            float cornerX = (impactPos.x > 0) ? 19.0f : -19.0f;
            float cornerZ = (impactPos.z > 0) ? 34.0f : -34.0f;
            return new Vector3(cornerX, impactPos.y, cornerZ);
        }
        return impactPos; // Throw-in
    }

    private Vector3 PositionBallAndTaker(Vector3 rawPos)
    {
        Vector3 directionToCenter = (Vector3.zero - rawPos).normalized;
        Vector3 bufferedBallPos = rawPos + (directionToCenter * 1.8f);
        bufferedBallPos.y = ballController.transform.localPosition.y;

        ballController.ResetBall(bufferedBallPos);

        if (setPieceTaker != null)
        {
            Vector3 takerPos = bufferedBallPos - (directionToCenter * 0.5f);
            setPieceTaker.transform.localPosition = takerPos;
            
            Vector3 lookDir = (bufferedBallPos - takerPos).normalized;
            if (lookDir != Vector3.zero)
            {
                setPieceTaker.transform.localRotation = Quaternion.LookRotation(lookDir);
            }
            setPieceTaker.SetState(AgentState.Stalled);
        }
        return bufferedBallPos;
    }

    private void ApplySetPieceRotationLimits(Vector3 rawPos)
    {
        float minAngle = 0f;
        float maxAngle = 360f;

        if (currentPlayType == PlayType.GoalKick)
        {
            if (rawPos.z > 0) { minAngle = 10f; maxAngle = 350f; }
            else { minAngle = 190f; maxAngle = 170f; }
        }
        else if (currentPlayType == PlayType.Corner)
        {
            if (rawPos.x < 0 && rawPos.z < 0) { minAngle = 0; maxAngle = 90; }      
            else if (rawPos.x > 0 && rawPos.z < 0) { minAngle = 270; maxAngle = 360; } 
            else if (rawPos.x < 0 && rawPos.z > 0) { minAngle = 90; maxAngle = 180; }  
            else if (rawPos.x > 0 && rawPos.z > 0) { minAngle = 180; maxAngle = 270; } 
        }
        else if (currentPlayType == PlayType.ThrowIn)
        {
            float deepZoneThreshold = 29.0f;
            bool isDeep = Mathf.Abs(rawPos.z) > deepZoneThreshold;

            if (rawPos.x > 0) // RIGHT Sideline
            {
                minAngle = 180; maxAngle = 360; 
                if (isDeep) { if (rawPos.z > 0) maxAngle = 270; else minAngle = 270; }
            } 
            else // LEFT Sideline
            {
                minAngle = 0; maxAngle = 180;
                if (isDeep) { if (rawPos.z > 0) minAngle = 90; else maxAngle = 90; }
            }                      
        }
        setPieceTaker.SetRotationLimits(minAngle, maxAngle);
    }

    private void EnforceClearanceZone(Vector3 localCenter, float radius)
    {
        float limitX = 19.0f; 
        float limitZ = 34.0f;

        foreach (var agent in masterAgentList)
        {
            if (agent == setPieceTaker || agent.IsBenched) continue;

            float dist = Vector3.Distance(agent.transform.localPosition, localCenter);
            if (dist < radius)
            {
                Vector3 pushDir = (agent.transform.localPosition - localCenter).normalized;
                if (pushDir == Vector3.zero) pushDir = Vector3.forward; 

                Vector3 targetPos = localCenter + (pushDir * radius);

                // Clamp to field bounds
                targetPos.x = Mathf.Clamp(targetPos.x, -limitX, limitX);
                targetPos.z = Mathf.Clamp(targetPos.z, -limitZ, limitZ);

                agent.transform.localPosition = new Vector3(targetPos.x, agent.transform.localPosition.y, targetPos.z);
                agent.playerRb.linearVelocity = Vector3.zero;
                agent.playerRb.angularVelocity = Vector3.zero;
            }
        }
    }

    public void BroadcastKickCooldown(Vector3 ballPos, float radius, float duration)
    {
        foreach (var agent in masterAgentList)
        {
            if (agent.gameObject.activeSelf && Vector3.Distance(agent.transform.position, ballPos) <= radius)
            {
                agent.ApplyExternalCooldown(duration);
            }
        }
    }
    #endregion

    #region Environment Reset & Scenarios
    public void ResetSceneForGoal()
    {
        // 1. Reset reward history (Critical to prevent teleport rewards)
        _lastAgentToBallDists.Clear();
        _lastBallToGoalDist = 999f; 
        
        _currentEnvironmentStep = 0;
        MaxEnvironmentSteps = (int)Academy.Instance.EnvironmentParameters.GetWithDefault("max_env_steps", 5000f);
        
        if (scenarios.Count == 0) return;

        float lessonValue = Academy.Instance.EnvironmentParameters.GetWithDefault("lesson_index", defaultScenarioIndex);
        int scenarioIndex = Mathf.Clamp(Mathf.FloorToInt(lessonValue), 0, scenarios.Count - 1);
        
        InterpretAndLoadScenario(scenarios[scenarioIndex]);
    }

    private void InterpretAndLoadScenario(Scenario data)
    {
        LogEvent($"<color=orange>[LOAD]</color> Loading Scenario: <b>{data.name}</b> (Index: {scenarios.IndexOf(data)})");
        
        bool validScenarioFound = false;
        int maxAttempts = _isFirstSpawn ? 1000 : 10;
        int attemptCount = 0;

        // 1. Role Swap Logic
        bool swapRoles = Random.value > 0.5f;

        Vector3 currentBallPos = Vector3.zero;
        Dictionary<AgentController, Vector3> currentPlannedPositions = new Dictionary<AgentController, Vector3>();
        Dictionary<AgentController, Vector3> currentPlannedRotations = new Dictionary<AgentController, Vector3>();

        while (!validScenarioFound && attemptCount < maxAttempts)
        {
            attemptCount++;
            currentPlannedPositions.Clear();
            currentPlannedRotations.Clear();

            // 2. Ball Positioning
            currentBallPos = GetRandomPosInZones(data.ballZones) + GetRandomOffset(data.ballJitter);
            if (swapRoles) currentBallPos.z *= -1;

            // 3. Agent Positioning Helper
            void PlanTeamInternal(List<AgentController> agents, List<Scenario.AgentSetup> setups, int teamId)
            {
                for (int i = 0; i < agents.Count; i++)
                {
                    if (i >= setups.Count || !setups[i].isActive) continue;
                    var s = setups[i];
                    Vector3 pos = Vector3.zero;

                    if (s.mode == Scenario.SpawnMode.Direct) pos = s.directPos;
                    else if (s.mode == Scenario.SpawnMode.Zone) pos = GetRandomPosInZones(s.zones);
                    else if (s.mode == Scenario.SpawnMode.AnchorTarget)
                    {
                        Vector3 a = GetAnchorPos(s.anchor, teamId, currentBallPos);
                        Vector3 t = GetAnchorPos(s.target, teamId, currentBallPos);
                        Vector3 dir = (t - a).normalized;
                        if (s.extendOutward) dir *= -1;
                        float dist = (s.maxDistance > s.minDistance) ? Random.Range(s.minDistance, s.maxDistance) : s.minDistance;
                        pos = a + (dir * dist) + (Vector3.Cross(dir, Vector3.up) * s.orthogonalOffset);
                    }

                    if (teamId == 1 && data.zMirrorForTeamB) pos.z *= -1;
                    pos += GetRandomOffset(s.jitter);
                    currentPlannedPositions[agents[i]] = pos;

                    Vector3 rot = Vector3.zero;
                    if (s.lookAt == Scenario.AgentSetup.LookAtTarget.Custom) rot = s.rotation;
                    else
                    {
                        Vector3 targetCoord = Vector3.zero;
                        switch (s.lookAt)
                        {
                            case Scenario.AgentSetup.LookAtTarget.Ball: targetCoord = currentBallPos; break;
                            case Scenario.AgentSetup.LookAtTarget.OpponentGoal: targetCoord = GetAnchorPos(Scenario.AnchorPoint.OppGoal, teamId, Vector3.zero); break;
                            case Scenario.AgentSetup.LookAtTarget.OwnGoal: targetCoord = GetAnchorPos(Scenario.AnchorPoint.OwnGoal, teamId, Vector3.zero); break;
                        }
                        Vector3 lookDir = (targetCoord - pos).normalized;
                        if (lookDir != Vector3.zero) rot = Quaternion.LookRotation(lookDir).eulerAngles;
                    }
                    rot.y += Random.Range(-s.rotationRandomness, s.rotationRandomness);
                    currentPlannedRotations[agents[i]] = rot;
                }
            }

            // 4. Apply Planning based on Roles
            if (swapRoles)
            {
                PlanTeamInternal(teamA_Agents, data.teamB, 0); 
                PlanTeamInternal(teamB_Agents, data.teamA, 1);
            }
            else
            {
                PlanTeamInternal(teamA_Agents, data.teamA, 0);
                PlanTeamInternal(teamB_Agents, data.teamB, 1);
            }

            // 5. Validation
            if (IsScenarioInsideField(currentBallPos, currentPlannedPositions))
            {
                validScenarioFound = true;
            }
        }

        if (validScenarioFound)
        {
            ApplyScenario(currentBallPos, currentPlannedPositions, currentPlannedRotations);
            _lastValidBallPos = currentBallPos;
            _lastValidAgentPositions = new Dictionary<AgentController, Vector3>(currentPlannedPositions);
            _lastValidAgentRotations = new Dictionary<AgentController, Vector3>(currentPlannedRotations);
            _isFirstSpawn = false;
        }
        else
        {
            ApplyScenario(_lastValidBallPos, _lastValidAgentPositions, _lastValidAgentRotations);
        }
    }

    private void ApplyScenario(Vector3 ballPos, Dictionary<AgentController, Vector3> positions, Dictionary<AgentController, Vector3> rotations)
    {
        // 1. CLEANUP PHASE: Unregister everyone from the PREVIOUS match
        // We explicitly tell every agent: "You are no longer in a group."
        // This clears their internal reference so they are free to join the new one.
        foreach (var agent in masterAgentList)
        {
            // It is safe to call Unregister even if they aren't in it; Unity handles the check.
            if (agent.CachedTeamId == 0) m_TeamAGroup.UnregisterAgent(agent);
            else m_TeamBGroup.UnregisterAgent(agent);
        }

        // 2. BALL RESET
        ballController.ResetBall(ballPos);

        // 3. SETUP PHASE: Position and Register only the ACTIVE players
        foreach (var agent in masterAgentList)
        {
            if (positions.ContainsKey(agent))
            {
                // A. Wake up & Position
                ResetAgent(agent, positions[agent], rotations[agent]);
                
                // B. Register to Group (The "Attendance Sheet")
                // Since we unregistered everyone in Step 1, this will now succeed.
                if (agent.CachedTeamId == 0) m_TeamAGroup.RegisterAgent(agent);
                else m_TeamBGroup.RegisterAgent(agent);
            }
            else
            {
                // C. Bench unused agents
                MoveToBench(agent);
            }
        }
    }

    private bool IsScenarioInsideField(Vector3 ball, Dictionary<AgentController, Vector3> agents)
    {
        if (!IsPointInsideField(ball, 1.5f)) return false;
        foreach (var pos in agents.Values)
        {
            if (!IsPointInsideField(pos, 1.5f)) return false;
        }
        return true;
    }

    private bool IsPointInsideField(Vector3 pos, float margin)
    {
        float halfW = 40f / 2f - margin;
        float halfL = 70f / 2f - margin;
        return pos.x >= -halfW && pos.x <= halfW && pos.z >= -halfL && pos.z <= halfL;
    }

    private Vector3 GetRandomPosInZones(Scenario.FieldZone selection)
    {
        List<Rect> validRects = new List<Rect>();
        float colW = 38f / 3f;
        float rowL = 68f / 3f;

        // Defensive Row (Z: -34 to -11.3)
        if ((selection & Scenario.FieldZone.DefLeft) != 0)   validRects.Add(new Rect(-19, -34, colW, rowL));
        if ((selection & Scenario.FieldZone.DefCenter) != 0) validRects.Add(new Rect(-19 + colW, -34, colW, rowL));
        if ((selection & Scenario.FieldZone.DefRight) != 0)  validRects.Add(new Rect(-19 + (colW * 2), -34, colW, rowL));

        // Midfield Row (Z: -11.3 to 11.3)
        if ((selection & Scenario.FieldZone.MidLeft) != 0)   validRects.Add(new Rect(-19, -11.3f, colW, rowL));
        if ((selection & Scenario.FieldZone.MidCenter) != 0) validRects.Add(new Rect(-19 + colW, -11.3f, colW, rowL));
        if ((selection & Scenario.FieldZone.MidRight) != 0)  validRects.Add(new Rect(-19 + (colW * 2), -11.3f, colW, rowL));

        // Attacking Row (Z: 11.3 to 34)
        if ((selection & Scenario.FieldZone.AttLeft) != 0)   validRects.Add(new Rect(-19, 11.3f, colW, rowL));
        if ((selection & Scenario.FieldZone.AttCenter) != 0) validRects.Add(new Rect(-19 + colW, 11.3f, colW, rowL));
        if ((selection & Scenario.FieldZone.AttRight) != 0)  validRects.Add(new Rect(-19 + (colW * 2), 11.3f, colW, rowL));

        if (validRects.Count == 0) return Vector3.zero;

        Rect chosen = validRects[Random.Range(0, validRects.Count)];
        return new Vector3(Random.Range(chosen.xMin, chosen.xMax), 0.1f, Random.Range(chosen.yMin, chosen.yMax));
    }

    private Vector3 GetAnchorPos(Scenario.AnchorPoint point, int teamId, Vector3 ballPos)
    {
        float goalZ = 34.5f; 
        switch (point)
        {
            case Scenario.AnchorPoint.Ball: return ballPos;
            case Scenario.AnchorPoint.OwnGoal: return new Vector3(0, 0, teamId == 0 ? -goalZ : goalZ);
            case Scenario.AnchorPoint.OppGoal: return new Vector3(0, 0, teamId == 0 ? goalZ : -goalZ);
            case Scenario.AnchorPoint.MidfieldCenter: return Vector3.zero;
            default: return Vector3.zero;
        }
    }
    #endregion

    #region Helpers & State Management
    public void UpdatePhase(bool team0In, bool team1In, List<AgentController> contenders)
    {
        // 1. Determine Phase
        GamePhase newPhase = (team0In && team1In) ? GamePhase.Contested :
                            team0In ? GamePhase.TeamAPossession :
                            team1In ? GamePhase.TeamBPossession : GamePhase.Loose;

        // 2. Build Signature (Name Check)
        string currentNames = "";
        if (contenders != null && contenders.Count > 0)
        {
            List<string> names = new List<string>();
            foreach (var a in contenders) names.Add(a.name);
            names.Sort(); 
            currentNames = string.Join(",", names);
        }
        string currentSignature = newPhase.ToString() + currentNames;

        // 3. Detect Change & Release Locks
        if (currentSignature != lastPhaseSignature)
        {
            lastPhaseSignature = currentSignature;
            currentPhase = newPhase;

            // Release Set Piece Taker if someone else touches it or phase shifts
            if (setPieceTaker != null && setPieceTaker.CurrentState == AgentState.Restricted)
            {
                foreach (var agent in contenders)
                {
                    if (agent != setPieceTaker)
                    {
                        setPieceTaker.SetRotationLimits(0f, 360f);
                        setPieceTaker.SetState(AgentState.Regular);
                        setPieceTaker = null; 
                        break;
                    }
                }
            }
            LogPhaseChange(contenders, newPhase);
        }
    }

    // --- Logging & Visuals ---

    private void LogPhaseChange(List<AgentController> contenders, GamePhase phase)
    {
        string namesDisplay = "";
        if (contenders != null && contenders.Count > 0)
        {
            List<string> formattedNames = new List<string>();
            foreach (var a in contenders)
            {
                string col = GetTeamHex(a.CachedTeamId);
                formattedNames.Add($"<color={col}>{a.name}</color>");
            }
            namesDisplay = " (" + string.Join(", ", formattedNames) + ")";
        }

        string phaseDesc = "";
        switch (phase)
        {
            case GamePhase.TeamAPossession: phaseDesc = $"<color={colorTeamA}>Team A</color> Possession{namesDisplay}"; break;
            case GamePhase.TeamBPossession: phaseDesc = $"<color={colorTeamB}>Team B</color> Possession{namesDisplay}"; break;
            case GamePhase.Contested: phaseDesc = $"<color={colorRules}>Contested</color>{namesDisplay}"; break;
            case GamePhase.Loose: phaseDesc = "Loose"; break;
        }
        LogEvent($"<color={colorPhase}>[PHASE]</color> {phaseDesc}");
    }

    private void LogEpisodeSummary()
    {
        string colorA = GetTeamHex(0);
        string colorB = GetTeamHex(1);

        string summary = $"<color={colorSystem}>[EPISODE END]</color> " +
                        $"<color={colorA}>Team A: {(_cumulatedRewardA >= 0 ? "+" : "")}{_cumulatedRewardA:F2}</color> | " +
                        $"<color={colorB}>Team B: {(_cumulatedRewardB >= 0 ? "+" : "")}{_cumulatedRewardB:F2}</color>";
        
        LogEvent(summary);
        _cumulatedRewardA = 0f;
        _cumulatedRewardB = 0f;
    }

    private void LogEvent(string message)
    {
        if (!muteLog) Debug.Log(message);
    }

    private string GetTeamHex(int teamId) => (teamId == 0) ? colorTeamA : colorTeamB;
    private string GetTeamName(int teamId) => (teamId == 0) ? "Team A" : "Team B";
    
    // --- Agent Management ---

    private int GetActiveAgentCount()
    {
        int count = 0;
        foreach (var agent in masterAgentList)
        {
            if (!agent.IsBenched) count++;
        }
        return count;
    }

    private void ResetAgent(AgentController agent, Vector3 pos, Vector3 rotEuler)
    {
        agent.IsBenched = false;
        agent.gameObject.SetActive(true);
        agent.GetComponent<MeshRenderer>().enabled = true;
        foreach (var col in agent.GetComponentsInChildren<Collider>()) col.enabled = true;

        agent.transform.localPosition = pos; 
        agent.transform.localRotation = Quaternion.Euler(rotEuler);
        
        // 1. Reset Physics
        agent.playerRb.linearVelocity = Vector3.zero;
        agent.playerRb.angularVelocity = Vector3.zero;

        // 2. CRITICAL FIX: "Spawn Silence"
        // Apply a tiny cooldown (e.g., 5 frames or 0.1s) to prevent "Ghost Kicks"
        // from the previous episode bleeding into this one.
        // This forces the agent to do nothing for the very first moment of the new round.
        if (agent.gameObject.activeInHierarchy)
        {
             // Assuming your AgentController has this method 
             // (based on your BroadcastKickCooldown usage)
            agent.ApplyExternalCooldown(0.1f); 
        }

        Physics.SyncTransforms();
    }

    private void MoveToBench(AgentController agent)
    {
        agent.IsBenched = true;
        agent.transform.localPosition = new Vector3(0, -50, 0); 
        agent.playerRb.linearVelocity = Vector3.zero;
        agent.GetComponent<MeshRenderer>().enabled = false;
        foreach (var col in agent.GetComponentsInChildren<Collider>()) col.enabled = false;
        agent.gameObject.SetActive(false); 
    }

    private AgentController GetClosestPlayerOnTeam(int teamId, Vector3 targetPos)
    {
        List<AgentController> team = (teamId == 0) ? teamA_Agents : teamB_Agents;
        AgentController closest = null;
        float minDist = float.MaxValue;

        foreach (var agent in team)
        {
            if (!agent.gameObject.activeSelf) continue;
            float d = Vector3.Distance(agent.transform.position, targetPos);
            if (d < minDist)
            {
                minDist = d;
                closest = agent;
            }
        }
        return closest;
    }

    // --- Manual Control ---

    void SwapControl()
    {
        int attempts = 0;
        do
        {
            currentControlIndex = (currentControlIndex + 1) % masterAgentList.Count;
            attempts++;
        } 
        while (!masterAgentList[currentControlIndex].gameObject.activeSelf && attempts < masterAgentList.Count);

        UpdateControlPermissions();

        AgentController active = masterAgentList[currentControlIndex];
        if (active.gameObject.activeSelf)
        {
            string teamCol = GetTeamHex(active.CachedTeamId);
            LogEvent($"<color={colorSystem}>[SYSTEM]</color> Control: <color={teamCol}>{active.name}</color> (<color={teamCol}>{GetTeamName(active.CachedTeamId)}</color>)");
        }
    }

    void UpdateControlPermissions()
    {
        for (int i = 0; i < masterAgentList.Count; i++)
        {
            masterAgentList[i].isControlActive = (i == currentControlIndex);
        }
    }

    private Vector3 GetRandomOffset(float radius)
    {
        if (radius <= 0) return Vector3.zero;
        Vector2 circle = Random.insideUnitCircle * radius;
        return new Vector3(circle.x, 0, circle.y);
    }
    #endregion
}