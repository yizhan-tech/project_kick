using UnityEngine;
using System.Collections.Generic;

public class BallController : MonoBehaviour
{
    [Header("References")]
    public EnvController envController;
    public Rigidbody rb;

    [Header("Status")]
    public AgentController lastTouchedBy;
    public int lastTouchedTeam = -1; 
    public string lastActionType = "None";

    [Header("Analyst Settings")]
    public float influenceRadius = 1.2f; 
    public LayerMask playerLayer;
    public float kickIgnoreDuration = 0.15f; 

    [Header("Analyst Results")]
    public int playersInRangeCount = 0;
    private Collider[] playersInRange;

    // --- INTERNAL TIMER VARIABLES ---
    private AgentController ignoreAgent;
    private float ignoreTimer = 0f;

    //======================================
    // Object State
    //======================================
    private void Awake()
    {
        if (rb == null) rb = GetComponent<Rigidbody>();
    }

    public void ResetBall(Vector3 localPosition)
    {
        // 1. Kill all momentum immediately
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;

        // 2. THE FIX: Use localPosition to stay inside the specific EnvRoot
        transform.localPosition = localPosition;
        
        // 3. Reset tracking data
        lastTouchedBy = null;
        lastTouchedTeam = -1;
        lastActionType = "None";
        
        ignoreAgent = null;
        ignoreTimer = 0f;

        // 4. Force Physics sync so the engine knows the ball moved
        Physics.SyncTransforms();
    }

    //======================================
    // The Sensor (Interactions)
    //======================================
    public void RegisterKick(AgentController agent)
    {
        SetOwnership(agent, "Kick");
        ignoreAgent = agent;
        ignoreTimer = kickIgnoreDuration;
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (collision.gameObject.CompareTag("Player"))
        {
            AgentController agent = collision.gameObject.GetComponent<AgentController>();
            if (agent != null)
            {
                if (agent == ignoreAgent && ignoreTimer > 0f) return;
                SetOwnership(agent, "Touch");
            }
        }
    }

    //======================================
    // The Analyst (Physics & State)
    //======================================
    private void FixedUpdate()
    {
        if (ignoreTimer > 0f)
        {
            ignoreTimer -= Time.fixedDeltaTime;
            if (ignoreTimer <= 0f) ignoreAgent = null; 
        }

        if (envController == null) return;
        
        // We still use OverlapSphere to get "Candidates" efficiently
        playersInRange = Physics.OverlapSphere(transform.position, influenceRadius + 1.0f, playerLayer); // Expanded radius to catch control points

        UpdatePossessionPhase();
    }

    public float GetForceMultiplier()
    {
        // Prevent DivideByZero if count is 0
        return playersInRangeCount > 0 ? 1f / playersInRangeCount : 1f;
    }

    public void UpdatePossessionPhase()
    {
        bool team0InRange = false;
        bool team1InRange = false;
        List<AgentController> contenders = new List<AgentController>();

        AgentController closestAgent = null;
        float minDistance = float.MaxValue;

        // Reset the official count before calculating
        playersInRangeCount = 0;

        foreach (var col in playersInRange)
        {
            AgentController agent = col.GetComponentInParent<AgentController>();
            if (agent != null)
            {
                if (agent.CurrentState == AgentState.Restricted) continue;

                // --- NEW LOGIC START ---
                // Calculate distance from Ball to the Agent's CONTROL POINT (feet/sweet spot)
                // rather than the agent's center.
                
                float distanceToControl = Vector3.Distance(transform.position, agent.controlPoint.position);

                // If the control point is within the influence radius, they are "In Contest"
                if (distanceToControl <= influenceRadius)
                {
                    playersInRangeCount++; // Increment count for Force Multiplier
                    
                    if (agent.CachedTeamId == 0) team0InRange = true;
                    if (agent.CachedTeamId == 1) team1InRange = true;
                    
                    if (!contenders.Contains(agent)) contenders.Add(agent);

                    // Track closest for Deflection/Touch logic
                    // (We stick to distanceToControl for consistency)
                    if (distanceToControl < minDistance)
                    {
                        minDistance = distanceToControl;
                        closestAgent = agent;
                    }
                }
                // --- NEW LOGIC END ---
            }
        }

        if (closestAgent != null)
        {
            if (closestAgent == ignoreAgent && ignoreTimer > 0f)
            {
                // Ignoring kicker
            }
            else
            {
                SetOwnership(closestAgent, "Deflection");
            }
        }

        envController.UpdatePhase(team0InRange, team1InRange, contenders);
    }

    //======================================
    // The Reporter (Triggers)
    //======================================
    private void OnTriggerEnter(Collider other)
    {
        if (envController == null) return;

        if (other.CompareTag("Goal_A"))
        {
            envController.ResolveGoal(1, lastTouchedBy, lastActionType);
        }
        else if (other.CompareTag("Goal_B"))
        {
            envController.ResolveGoal(0, lastTouchedBy, lastActionType);
        }
        else if (other.CompareTag("OOB"))
        {
            envController.ResolveOutOfBounds(
                lastTouchedBy, 
                lastTouchedTeam, 
                lastActionType, 
                transform.position
            );
        }
    }

    private void SetOwnership(AgentController agent, string action)
    {
        lastTouchedBy = agent;
        lastTouchedTeam = agent.CachedTeamId;
        lastActionType = action;
    }
}