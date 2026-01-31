using System.Collections.Generic;
using UnityEngine;
using Unity.MLAgents;

public enum GamePhase { Loose, TeamAPossession, TeamBPossession, Contested }
public enum PlayType { OpenPlay, GoalKick, Corner, ThrowIn }

public class EnvController : MonoBehaviour
{
    [Header("Curriculum Settings")]
    public List<Scenario> scenarios; 
    public int defaultScenarioIndex = 0; // Fallback index if training isn't active

    [Header("References")]
    public BallController ballController;
    // Direct references to the agents in your scene
    public List<AgentController> teamA_Agents; 
    public List<AgentController> teamB_Agents;

    [Header("Play State")]
    public PlayType currentPlayType = PlayType.OpenPlay;
    public GamePhase currentPhase = GamePhase.Loose;

    [Header("Set Piece Logic")]
    public AgentController setPieceTaker;
    public float stallTimer = 0f;
    public float simulatedRunSpeed = 6f; 

    [Header("Match Settings")]
    public int MaxEnvironmentSteps = 5000; // Central limit
    private int _currentEnvironmentStep = 0;

    [Header("Debug Settings")]
    public bool muteLog = false;

    private List<AgentController> masterAgentList = new List<AgentController>();
    private int currentControlIndex = 0;

    // --- SCENARIO MEMORY ---
    private bool _isFirstSpawn = true; 
    private Vector3 _lastValidBallPos = new Vector3(0, 0.1f, 0);
    private Dictionary<AgentController, Vector3> _lastValidAgentPositions = new Dictionary<AgentController, Vector3>();
    private Dictionary<AgentController, Vector3> _lastValidAgentRotations = new Dictionary<AgentController, Vector3>();

    private SimpleMultiAgentGroup m_TeamAGroup;
    private SimpleMultiAgentGroup m_TeamBGroup;

    private float _cumulatedRewardA = 0f;
    private float _cumulatedRewardB = 0f;

    private void AddRewardA(float amount) 
    { 
        m_TeamAGroup.AddGroupReward(amount); 
        _cumulatedRewardA += amount; 
    }

    private void AddRewardB(float amount) 
    { 
        m_TeamBGroup.AddGroupReward(amount); 
        _cumulatedRewardB += amount; 
    }

    // Standard Hex Colors
    private string colorTeamA = "#FF4C4C"; 
    private string colorTeamB = "#4C91FF"; 
    private string colorSystem = "#FFFF00"; 
    private string colorPhase = "#00FF00"; 
    private string colorRules = "#FFA500"; 

    //======================================
    // Registry
    //======================================

    private int GetActiveAgentCount()
    {
        int count = 0;
        foreach (var agent in masterAgentList)
        {
            // FIX: We cannot use .activeSelf anymore because we use "Ghosting".
            // We must check if they are NOT benched.
            if (!agent.IsBenched) count++;
        }
        return count;
    }
    
    void Awake()
    {
        m_TeamAGroup = new SimpleMultiAgentGroup();
        m_TeamBGroup = new SimpleMultiAgentGroup();

        foreach (var agent in teamA_Agents) {
            RegisterAgent(agent, 0);
            m_TeamAGroup.RegisterAgent(agent);
        }
        foreach (var agent in teamB_Agents) {
            RegisterAgent(agent, 1);
            m_TeamBGroup.RegisterAgent(agent);
        }
        
        UpdateControlPermissions();

        // 2. Pre-flight Hide: Move them to the bench instead of SetActive(false)
        foreach (var agent in masterAgentList)
        {
            // Use a ghosting method instead of disabling the GameObject
            MoveToBench(agent); 
        }
    }

    void Start()
    {
        // 3. Delay: Wait for ML-Agents Academy to sync the "lesson_index"
        StartCoroutine(InitialResetBuffer());
    }

    private System.Collections.IEnumerator InitialResetBuffer()
    {
        // Two frames is the "sweet spot" to ensure curriculum data has arrived
        yield return new WaitForEndOfFrame();
        yield return new WaitForEndOfFrame();
        
        // 4. Load: Now call the scenario. It will only SetActive(true) the agents it needs.
        ResetSceneForGoal();
        LogEvent("<color=#FFFF00>[SYSTEM]</color> Environment Initialized via Scenario.");
    }

    void RegisterAgent(AgentController agent, int teamId)
    {
        if (agent != null)
        {
            agent.CachedTeamId = teamId;
            agent.env = this;
            masterAgentList.Add(agent);
        }
    }

    private string GetTeamHex(int teamId) => (teamId == 0) ? colorTeamA : colorTeamB;
    private string GetTeamName(int teamId) => (teamId == 0) ? "Team A" : "Team B";

    //======================================
    // Control
    //======================================
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.H)) SwapControl();
    }

    void FixedUpdate()
    {
        if (stallTimer > 0) stallTimer -= Time.fixedDeltaTime;

        // ... (Your setPieceTaker logic) ...

        _currentEnvironmentStep++;

        if (GetActiveAgentCount() <= 2)
        {
            ApplyColdStartProximityRewards();
        }
        
        if (_currentEnvironmentStep >= MaxEnvironmentSteps)
        {
            // GATING: Only end if we haven't already reset this frame
            if (_currentEnvironmentStep > 0) 
            {
                m_TeamAGroup.EndGroupEpisode(); 
                m_TeamBGroup.EndGroupEpisode();
                LogEpisodeSummary();
                ResetSceneForGoal();
            }
        }
    }

    void SwapControl()
    {
        // Only cycle through ACTIVE agents
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

    //======================================
    // Rules & Phase Logic
    //======================================
    private void LogEvent(string message)
    {
        if (!muteLog) Debug.Log(message);
    }

    private string lastPhaseSignature = "";

    public void UpdatePhase(bool team0In, bool team1In, List<AgentController> contenders)
    {
        // 1. Determine Phase
        GamePhase newPhase = (team0In && team1In) ? GamePhase.Contested :
                            team0In ? GamePhase.TeamAPossession :
                            team1In ? GamePhase.TeamBPossession : GamePhase.Loose;

        // 2. Build Signature
        string currentNames = "";
        if (contenders != null && contenders.Count > 0)
        {
            List<string> names = new List<string>();
            foreach (var a in contenders) names.Add(a.name);
            names.Sort(); 
            currentNames = string.Join(",", names);
        }
        string currentSignature = newPhase.ToString() + currentNames;

        // 3. Detect Change
        if (currentSignature != lastPhaseSignature)
        {
            lastPhaseSignature = currentSignature;
            currentPhase = newPhase;

            // --- DOUBLE-TOUCH / RESTRICTION RELEASE ---
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

            // Logging
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
            switch (currentPhase)
            {
                case GamePhase.TeamAPossession: phaseDesc = $"<color={colorTeamA}>Team A</color> Possession{namesDisplay}"; break;
                case GamePhase.TeamBPossession: phaseDesc = $"<color={colorTeamB}>Team B</color> Possession{namesDisplay}"; break;
                case GamePhase.Contested: phaseDesc = $"<color={colorRules}>Contested</color>{namesDisplay}"; break;
                case GamePhase.Loose: phaseDesc = "Loose"; break;
            }
            LogEvent($"<color={colorPhase}>[PHASE]</color> {phaseDesc}");
        }

        float possessionStrength = 0.0001f; 

        // Only give possession rewards during active gameplay
        if (currentPlayType == PlayType.OpenPlay)
        {

            if (currentPhase == GamePhase.TeamAPossession)
            {
                AddRewardA(possessionStrength);
            }
            else if (currentPhase == GamePhase.TeamBPossession)
            {
                AddRewardB(possessionStrength);
            }
        }
    }

    public void ResolveGoal(int scoringTeam, AgentController scorer, string action)
    {
        // ANTI-SPAM GATE: If step is 0, we just reset, so ignore this trigger
        if (_currentEnvironmentStep <= 0) return;

        LogEvent($"<color=cyan>[GOAL TRIGGER]</color> Team {scoringTeam} scored! Step: {_currentEnvironmentStep}");

        float efficiencyReward = Mathf.Clamp(1.0f - ((float)_currentEnvironmentStep / MaxEnvironmentSteps), 0.1f, 1.0f);

        if (scoringTeam == 0) {
            AddRewardA(efficiencyReward);
            AddRewardB(-1.0f);
        } else {
            AddRewardB(efficiencyReward);
            AddRewardA(-1.0f);
        }

        // --- GROUP INTEGRITY CHECK ---
        int countA = 0;
        int countB = 0;

        foreach (var agent in teamA_Agents)
        {
            // If the agent is disabled, the Group often ignores the EndEpisode signal
            if (agent != null && agent.gameObject.activeInHierarchy) countA++;
        }

        foreach (var agent in teamB_Agents)
        {
            if (agent != null && agent.gameObject.activeInHierarchy) countB++;
        }

        // THE HANDSHAKE
        m_TeamAGroup.EndGroupEpisode();
        m_TeamBGroup.EndGroupEpisode();

        LogEpisodeSummary();
        ResetSceneForGoal();
    }

    public void ResolveOutOfBounds(AgentController responsibleAgent, int teamId, string action, Vector3 oobLocation)
    {
        // 1. Immediate conversion to Local Space
        Vector3 localOOB = transform.InverseTransformPoint(oobLocation);

        if (GetActiveAgentCount() < 4)
        {
            m_TeamAGroup.EndGroupEpisode();
            m_TeamBGroup.EndGroupEpisode();
            LogEpisodeSummary();
            ResetSceneForGoal();
            return; 
        }

        currentPlayType = DetermineRestartType(teamId, localOOB);
        int beneficiaryId = (teamId == 0) ? 1 : 0;

        // Finding the closest player is still easiest in World Space
        setPieceTaker = GetClosestPlayerOnTeam(beneficiaryId, oobLocation); 
        
        if (setPieceTaker != null) {
            float distance = Vector3.Distance(setPieceTaker.transform.position, oobLocation);
            stallTimer = distance / simulatedRunSpeed;
        }

        LogEvent($"<color={colorRules}>[OOB]</color> Restart: {currentPlayType}");
        
        ResetSceneForSetPiece(localOOB);
    }

    private void ApplyColdStartProximityRewards()
    {
        foreach (var agent in masterAgentList)
        {
            // Only reward agents currently on the pitch
            if (agent.IsBenched || !agent.gameObject.activeInHierarchy) continue;

            float dist = Vector3.Distance(agent.transform.localPosition, ballController.transform.localPosition);
            
            // 1.2f is roughly 'possession' range. 10f is the 'search' radius.
            if (dist > 1.2f && dist < 10f)
            {
                // The reward gets higher as distance decreases: $0.0002 \times (1.0 - (\text{dist} / 10.0))$
                float shaping = 0.0005f * (1.0f - (dist / 10.0f));
                
                if (agent.CachedTeamId == 0) AddRewardA(shaping);
                else AddRewardB(shaping);
            }
        }
    }

    //======================================
    // Scenario Loader (Episode Reset)
    //======================================
    public void ResetSceneForGoal()
    {
        // 1. Close the gate immediately
        _currentEnvironmentStep = 0;

        // 2. Refresh steps from Academy
        MaxEnvironmentSteps = (int)Academy.Instance.EnvironmentParameters.GetWithDefault("max_env_steps", 5000f);
        
        if (scenarios.Count == 0) return;

        // 3. Load Scenario
        float lessonValue = Academy.Instance.EnvironmentParameters.GetWithDefault("lesson_index", defaultScenarioIndex);
        int scenarioIndex = Mathf.Clamp(Mathf.FloorToInt(lessonValue), 0, scenarios.Count - 1);
        
        InterpretAndLoadScenario(scenarios[scenarioIndex]);
    }

    private void ResetAgent(AgentController agent, Vector3 pos, Vector3 rotEuler)
    {
        agent.IsBenched = false;
        
        // 1. Re-enable visuals and physics
        agent.GetComponent<MeshRenderer>().enabled = true;
        foreach (var col in agent.GetComponentsInChildren<Collider>()) col.enabled = true;

        // 2. Restore position and momentum
        agent.transform.localPosition = pos; 
        agent.transform.localRotation = Quaternion.Euler(rotEuler);
        agent.playerRb.linearVelocity = Vector3.zero;
        agent.playerRb.angularVelocity = Vector3.zero;

        Physics.SyncTransforms();
    }

    private void MoveToBench(AgentController agent)
    {
        agent.IsBenched = true;
        
        // 1. Move them away
        agent.transform.localPosition = new Vector3(0, -50, 0); 
        agent.playerRb.linearVelocity = Vector3.zero;

        // 2. Disable physical presence but keep the script alive
        agent.GetComponent<MeshRenderer>().enabled = false;
        foreach (var col in agent.GetComponentsInChildren<Collider>()) col.enabled = false;
        
        // IMPORTANT: Keep the GameObject active
        agent.gameObject.SetActive(true); 
    }

    private Vector3 GetRandomOffset(float radius)
    {
        if (radius <= 0) return Vector3.zero;
        Vector2 circle = Random.insideUnitCircle * radius;
        return new Vector3(circle.x, 0, circle.y);
    }

    private void LogEpisodeSummary()
    {
        string colorA = GetTeamHex(0);
        string colorB = GetTeamHex(1);

        string summary = $"<color={colorSystem}>[EPISODE END]</color> " +
                        $"<color={colorA}>Team A: {(_cumulatedRewardA >= 0 ? "+" : "")}{_cumulatedRewardA:F2}</color> | " +
                        $"<color={colorB}>Team B: {(_cumulatedRewardB >= 0 ? "+" : "")}{_cumulatedRewardB:F2}</color>";
        
        LogEvent(summary);

        // Reset trackers for the next episode
        _cumulatedRewardA = 0f;
        _cumulatedRewardB = 0f;
    }

    //======================================
    // Set Piece Logic (In-Game Resets)
    //======================================
    private AgentController GetClosestPlayerOnTeam(int teamId, Vector3 targetPos)
    {
        List<AgentController> team = (teamId == 0) ? teamA_Agents : teamB_Agents;
        AgentController closest = null;
        float minDist = float.MaxValue;

        foreach (var agent in team)
        {
            // Ignore benched players
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

    public void BroadcastKickCooldown(Vector3 ballPos, float radius, float duration)
    {
        foreach (var agent in masterAgentList)
        {
            // Only affect active players
            if (agent.gameObject.activeSelf && Vector3.Distance(agent.transform.position, ballPos) <= radius)
            {
                agent.ApplyExternalCooldown(duration);
            }
        }
    }

    public void ResetSceneForSetPiece(Vector3 oobImpact)
    {
        // 1. Determine where the set piece *starts* (Corner flag? Goal line?)
        Vector3 rawRestartPos = CalculateBaseRestartLocation(oobImpact);

        // 2. Position the Ball and the Taker safely inside the field
        // Returns the actual location of the ball after buffering
        Vector3 bufferedBallPos = PositionBallAndTaker(rawRestartPos);

        if (setPieceTaker != null)
        {
            // 3. Lock the Taker's rotation based on the rules
            ApplySetPieceRotationLimits(rawRestartPos);

            // 4. Push other agents away (The "Center Magnet" logic)
            EnforceClearanceZone(setPieceTaker.transform.position, 5.0f);
        }
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
        
        // Throw-ins just use the impact point
        return impactPos;
    }

    private Vector3 PositionBallAndTaker(Vector3 rawPos)
    {
        // directionToCenter is now relative to the local (0,0,0) of this EnvRoot
        Vector3 directionToCenter = (Vector3.zero - rawPos).normalized;
        Vector3 bufferedBallPos = rawPos + (directionToCenter * 1.8f);
        bufferedBallPos.y = ballController.transform.localPosition.y;

        ballController.ResetBall(bufferedBallPos);

        if (setPieceTaker != null)
        {
            // FIX: Use transform.localPosition
            Vector3 takerPos = bufferedBallPos - (directionToCenter * 0.5f);
            setPieceTaker.transform.localPosition = takerPos;
            
            // FIX: LookAt uses World Space. We must calculate a local rotation instead.
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
            // Goal Kick: 10-350 or 190-170
            if (rawPos.z > 0) { minAngle = 10f; maxAngle = 350f; }
            else { minAngle = 190f; maxAngle = 170f; }
        }
        else if (currentPlayType == PlayType.Corner)
        {
            // Corner: 90 Degree Quadrants
            if (rawPos.x < 0 && rawPos.z < 0) { minAngle = 0; maxAngle = 90; }      
            else if (rawPos.x > 0 && rawPos.z < 0) { minAngle = 270; maxAngle = 360; } 
            else if (rawPos.x < 0 && rawPos.z > 0) { minAngle = 90; maxAngle = 180; }  
            else if (rawPos.x > 0 && rawPos.z > 0) { minAngle = 180; maxAngle = 270; } 
        }
        else if (currentPlayType == PlayType.ThrowIn)
        {
            // Throw-In: Dynamic Deep Zones
            float deepZoneThreshold = 29.0f;
            bool isDeep = Mathf.Abs(rawPos.z) > deepZoneThreshold;

            if (rawPos.x > 0) // RIGHT Sideline
            {
                minAngle = 180; maxAngle = 360; 
                if (isDeep)
                {
                    if (rawPos.z > 0) maxAngle = 270; // Top Right -> Block Up
                    else minAngle = 270;              // Bot Right -> Block Down
                }
            } 
            else // LEFT Sideline
            {
                minAngle = 0; maxAngle = 180;
                if (isDeep)
                {
                    if (rawPos.z > 0) minAngle = 90; // Top Left -> Block Up
                    else maxAngle = 90;              // Bot Left -> Block Down
                }
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
            // Ignore benched or the taker
            if (agent == setPieceTaker || agent.IsBenched) continue;

            // Everything here is now Local Space
            float dist = Vector3.Distance(agent.transform.localPosition, localCenter);
            if (dist < radius)
            {
                Vector3 pushDir = (agent.transform.localPosition - localCenter).normalized;
                if (pushDir == Vector3.zero) pushDir = Vector3.forward; 

                Vector3 targetPos = localCenter + (pushDir * radius);

                // Clamp locally
                targetPos.x = Mathf.Clamp(targetPos.x, -limitX, limitX);
                targetPos.z = Mathf.Clamp(targetPos.z, -limitZ, limitZ);

                agent.transform.localPosition = new Vector3(targetPos.x, agent.transform.localPosition.y, targetPos.z);
                
                agent.playerRb.linearVelocity = Vector3.zero;
                agent.playerRb.angularVelocity = Vector3.zero;
            }
        }
    }
    private Vector3 GetRandomPosInZones(Scenario.FieldZone selection)
    {
        List<Rect> validRects = new List<Rect>();
        
        // We divide the field into 3 columns (X) and 3 rows (Z)
        // Width: -19 to 19 (Total 38), Length: -34 to 34 (Total 68)
        float colW = 38f / 3f;
        float rowL = 68f / 3f;

        // Mapping bits to rectangles
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
        float goalZ = 34.5f; // Center of the goal line
        switch (point)
        {
            case Scenario.AnchorPoint.Ball: return ballPos;
            case Scenario.AnchorPoint.OwnGoal: return new Vector3(0, 0, teamId == 0 ? -goalZ : goalZ);
            case Scenario.AnchorPoint.OppGoal: return new Vector3(0, 0, teamId == 0 ? goalZ : -goalZ);
            case Scenario.AnchorPoint.MidfieldCenter: return Vector3.zero;
            default: return Vector3.zero;
        }
    }

    private void InterpretAndLoadScenario(Scenario data)
    {
        LogEvent($"<color=orange>[LOAD]</color> Loading Scenario: <b>{data.name}</b> (Index: {scenarios.IndexOf(data)})");
        
        bool validScenarioFound = false;
        int maxAttempts = _isFirstSpawn ? 1000 : 10;
        int attemptCount = 0;

        // 1. Decide if we are swapping roles for this episode
        bool swapRoles = Random.value > 0.5f;

        Vector3 currentBallPos = Vector3.zero;
        Dictionary<AgentController, Vector3> currentPlannedPositions = new Dictionary<AgentController, Vector3>();
        Dictionary<AgentController, Vector3> currentPlannedRotations = new Dictionary<AgentController, Vector3>();

        while (!validScenarioFound && attemptCount < maxAttempts)
        {
            attemptCount++;
            currentPlannedPositions.Clear();
            currentPlannedRotations.Clear();

            // 2. GENERATE BALL POSITION
            // If swapped, we mirror the ball's zone/jitter to the other side of the field
            currentBallPos = GetRandomPosInZones(data.ballZones) + GetRandomOffset(data.ballJitter);
            if (swapRoles) currentBallPos.z *= -1;

            // Internal helper remains the same, it uses TeamId to find the right Goal Anchors
            void PlanTeamInternal(List<AgentController> agents, List<Scenario.AgentSetup> setups, int teamId)
            {
                for (int i = 0; i < agents.Count; i++)
                {
                    if (i >= setups.Count || !setups[i].isActive) continue;
                    var s = setups[i];
                    Vector3 pos = Vector3.zero;

                    // AnchorTarget logic is the key: 
                    // GetAnchorPos(OwnGoal, Team 0) = -35
                    // GetAnchorPos(OwnGoal, Team 1) = +35
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

                    // Rotation: Always face the intended target relative to YOUR team identity
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

            // 3. THE "ROLE" SWAP
            if (swapRoles)
            {
                // Team A (Red) uses instructions intended for B (Defender)
                // Team B (Blue) uses instructions intended for A (Attacker)
                PlanTeamInternal(teamA_Agents, data.teamB, 0); 
                PlanTeamInternal(teamB_Agents, data.teamA, 1);
            }
            else
            {
                // Standard: A is Attacker, B is Defender
                PlanTeamInternal(teamA_Agents, data.teamA, 0);
                PlanTeamInternal(teamB_Agents, data.teamB, 1);
            }

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

    private bool IsScenarioInsideField(Vector3 ball, Dictionary<AgentController, Vector3> agents)
    {
        // Use a 1.0m margin so cubes don't clip through walls
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

    private void ApplyScenario(Vector3 ballPos, Dictionary<AgentController, Vector3> positions, Dictionary<AgentController, Vector3> rotations)
    {
        ballController.ResetBall(ballPos);
        foreach (var agent in masterAgentList)
        {
            if (positions.ContainsKey(agent))
            {
                ResetAgent(agent, positions[agent], rotations[agent]);
            }
            else
            {
                MoveToBench(agent);
            }
        }
    }
}