using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Manages the physical state, ownership tracking, and phase detection of the soccer ball.
/// Acts as the central sensor for the Environment Controller to determine game states.
/// </summary>
public class BallController : MonoBehaviour
{
    // =================================================================================================================
    // 1. INSPECTOR SETTINGS & REFERENCES
    // =================================================================================================================
    
    [Header("References")]
    public EnvController envController;
    public Rigidbody rb;

    [Header("Status (Read Only)")]
    public AgentController lastTouchedBy;
    public int lastTouchedTeam = -1; 
    public string lastActionType = "None";

    [Header("Analyst Settings")]
    [Tooltip("Radius around the ball where an agent is considered 'In Contest'.")]
    public float influenceRadius = 1.2f; 
    public LayerMask playerLayer;
    [Tooltip("Time in seconds to ignore collisions from the kicker immediately after a kick.")]
    public float kickIgnoreDuration = 0.15f; 

    [Header("Analyst Results")]
    public int playersInRangeCount = 0;
    private Collider[] playersInRange;

    // --- Internal Timer Variables ---
    private AgentController ignoreAgent;
    private float ignoreTimer = 0f;

    // =================================================================================================================
    // 2. LIFECYCLE & INITIALIZATION
    // =================================================================================================================
    
    /// <summary>
    /// Resets the ball's physics and state for a new episode or set piece.
    /// </summary>
    /// <param name="localPosition">The target position relative to the EnvRoot.</param>
    public void ResetBall(Vector3 localPosition)
    {
        // 1. Kill all momentum immediately
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        
        // 2. Set position relative to the parent environment
        transform.localPosition = localPosition;
        
        // 3. Reset tracking data
        lastTouchedBy = null;
        lastTouchedTeam = -1;
        lastActionType = "None";
        
        ignoreAgent = null;
        ignoreTimer = 0f;

        // 4. Force Physics sync so the engine knows the ball moved before the next FixedUpdate
        Physics.SyncTransforms();
    }

    private void FixedUpdate()
    {
        // Handle kick cooldown timer
        if (ignoreTimer > 0f)
        {
            ignoreTimer -= Time.fixedDeltaTime;
            if (ignoreTimer <= 0f) ignoreAgent = null; 
        }

        if (envController == null) return;
        
        // Efficiently find all colliders near the ball
        // Radius is expanded slightly to ensure we catch agent control points
        playersInRange = Physics.OverlapSphere(transform.position, influenceRadius + 1.0f, playerLayer); 

        UpdatePossessionPhase();
    }

    // =================================================================================================================
    // 3. THE SENSOR (Interaction Logic)
    // =================================================================================================================

    /// <summary>
    /// Called by an Agent when it performs a deliberate kick action.
    /// Temporarily ignores physics collisions with the kicker to prevent jitter.
    /// </summary>
    public void RegisterKick(AgentController agent)
    {
        SetOwnership(agent, "Kick");
        ignoreAgent = agent;
        ignoreTimer = kickIgnoreDuration;
    }

    /// <summary>
    /// Detects physical collisions with agents (dribbling/incidental contact).
    /// </summary>
    private void OnCollisionEnter(Collision collision)
    {
        if (collision.gameObject.CompareTag("Player"))
        {
            AgentController agent = collision.gameObject.GetComponent<AgentController>();
            if (agent != null)
            {
                // If this is the agent who just kicked, ignore the immediate physical recoil
                if (agent == ignoreAgent && ignoreTimer > 0f) return;
                
                SetOwnership(agent, "Touch");
            }
        }
    }

    private void SetOwnership(AgentController agent, string action)
    {
        lastTouchedBy = agent;
        lastTouchedTeam = agent.CachedTeamId;
        lastActionType = action;
    }

    // =================================================================================================================
    // 4. THE ANALYST (Phase & Contest Logic)
    // =================================================================================================================

    /// <summary>
    /// Returns a multiplier (1 / Count) to distribute force when multiple agents crowd the ball.
    /// </summary>
    public float GetForceMultiplier()
    {
        return playersInRangeCount > 0 ? 1f / playersInRangeCount : 1f;
    }

    /// <summary>
    /// Analyzes nearby agents to determine if the ball is Loose, Possessed, or Contested.
    /// Updates the EnvController with the current phase.
    /// </summary>
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

                // --- POSSESSION LOGIC ---
                // Calculate distance from Ball to the Agent's CONTROL POINT (feet)
                float distanceToControl = Vector3.Distance(transform.position, agent.controlPoint.position);

                // If the control point is within the influence radius, they are "In Contest"
                if (distanceToControl <= influenceRadius)
                {
                    playersInRangeCount++;
                    
                    if (agent.CachedTeamId == 0) team0InRange = true;
                    if (agent.CachedTeamId == 1) team1InRange = true;
                    
                    if (!contenders.Contains(agent)) contenders.Add(agent);

                    // Track closest agent for Deflection logic
                    if (distanceToControl < minDistance)
                    {
                        minDistance = distanceToControl;
                        closestAgent = agent;
                    }
                }
            }
        }

        // Handle implicit "Deflection" ownership if an agent is close but didn't explicitly kick
        if (closestAgent != null)
        {
            if (closestAgent == ignoreAgent && ignoreTimer > 0f)
            {
                // Ignoring recent kicker
            }
            else
            {
                SetOwnership(closestAgent, "Deflection");
            }
        }

        // Send data to the referee (EnvController)
        envController.UpdatePhase(team0InRange, team1InRange, contenders);
    }

    // =================================================================================================================
    // 5. THE REPORTER (Trigger Detection)
    // =================================================================================================================

    private void OnTriggerEnter(Collider other)
    {
        if (envController == null) return;

        // GOAL DETECTION
        if (other.CompareTag("Goal_A"))
        {
            // Goal A hit -> Team B (Team 1) Scores
            envController.ResolveGoal(1, lastTouchedBy, lastActionType);
        }
        else if (other.CompareTag("Goal_B"))
        {
            // Goal B hit -> Team A (Team 0) Scores
            envController.ResolveGoal(0, lastTouchedBy, lastActionType);
        }
        // OUT OF BOUNDS DETECTION
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
}