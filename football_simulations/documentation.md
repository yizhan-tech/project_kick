# Introduction

This project serves as a foundation for Multi-Agent Reinforcement Learning (MARL) within a soccer environment. The core design philosophy is the abstraction of physical complexity in favor of tactical decision-making, thus providing a clear signal for agents to learn higher-level coordination and strategy.

# 1. Physical Environment Specification

The simulation environment is hosted within a hierarchical structure in Unity, rooted at the `EnvRoot` coordinate system. This organization ensures that all physical interactions and spatial coordinates are localized and consistent across multiple parallel training instances.

```
EnvRoot
├── Pitch
|   ├── Ground
|   ├── FieldLine_N
|   ├── FieldLine_S
|   ├── FieldLine_W
|   ├── FieldLine_E
|   ├── Wall_N
|   ├── Wall_S
|   ├── Wall_W
|   └── Wall_E
├── OOBRoot
|   ├── OOB_E
|   ├── OOB_W
|   ├── OOB_NW
|   ├── OOB_SW
|   ├── OOB_SE
|   └── OOB_NE
├── Goal_A
|   └── GoalTrigger
|   ├── GoalArea_A
|   ├── GoalPostL_A
|   └── GoalPostR_A
├── Goal_B
|   ├── GoalTrigger
|   ├── GoalArea_B
|   ├── GoalPostL_B
|   └── GoalPostR_B
├── Ball
└── Teams
    ├── Team_A
    |   ├── Player_A0
    |   ├── Player_A1
    |   └── Player_A2
    └── Team_B
        ├── Player_B0
        ├── Player_B1
        └── Player_B2
```

## 1.1 The Play Area (Pitch)

The `Pitch` defines the geometric and logical boundaries of the simulation. 

### 1.1.1 Spatial Dimensions and Geometry

The field of play is built upon a 40m x 70m static surface.
- Surface (`Ground`): A static plane providing a total interaction area of 2,800m².
- Containment (`Wall`): Four physical barriers positioned at the exact perimeter. These serve as a "hard-stop" for the physics engine, preventing objects from exiting the valid simulation space.
- Visual Indicators (`FieldLines`): Non-collidable markers placed slightly inward from the walls. This provides a visual buffer so that "Out of Bounds" events occur logically when the user sees the ball cross the white line.

### 1.1.2 Goal Structures

Each end of the pitch contains a goal assembly designed for binary detection and realistic ball deflections.
- Detection Volume (`GoalTrigger`): A box-collider trigger placed behind the goal line. Contact with the `Ball` layer signals a scoring event.
- Physical Posts (`GoalPost`): Two active cylinders per goal. These simulate the "bounce-back" effect. By excluding a horizontal crossbar, the simulation focuses on ground-based tactical play.
- Zone Visualization (`GoalArea`): Static mesh overlays used for visual orientation; these have no impact on physics calculation.

### 1.1.3 The Out-of-Bounds (OOB) System

The logical state of the game is governed by the `OOBRoot`, a system of intangible trigger volumes.

>**The "Full Clearance" Rule**: OOB triggers are mapped precisely to the 40m x 70m perimeter. Since the ball has a diameter of 1.2m, the physics engine triggers a reset the moment the ball's surface (edge) touches the trigger volume. This effectively creates an "Active Play Zone" of 37.6m x 67.6m for the ball's center, ensuring the ball is visually across the line before the whistle blows.

![OOB_vs_Wall](./media/oob_vs_wall.png)

<p align="center"><i>Fig 1. A visual comparison between OOB and Wall objects</i></p>

## 1.2 Dynamic Entities

The simulation tracks two primary dynamic classes: the Ball and the Agents. These entities are the only objects with active `Rigidbody` components during play.

### 1.2.1 The Ball

The central objective of the simulation.
- **Scale**: 1.2m Diameter.
- **Interaction Logic**: The ball serves as the "Reward Vector." Its position relative to the goals and agents forms the core of the proximity-based reward signals.

### 1.2.2 The Agents

Decision-making entities represented as 1.2m x 1.2m x 1.2m cubes.
- **Team Orientation**: Agents are categorized into `Team A` (Red) and `Team B` (Blue).
- **The Control Point**: A dedicated `Transform` located at the front-base of the agent. This represents the "Sweet Spot" for both the Ball Attraction Force (Dribbling) and the Kick Impulse (Striking/Tackling).

## 1.3 Physic Materials and Constants

To ensure deterministic and realistic behavior, we utilize specific `PhysicMaterial` configurations to define surface friction and energy restitution (Bounciness).

| **Object** | **Dynamic Friction** | **Static Friction** | **Bounciness** | **Friction Combine** | **Bounce Combine** |
| --- | --- | --- | --- | --- | --- |
| `Ground` | 0.8 | 0.8 | 0 | Multiply | Minimum |
| `Ball` | 0.6 | 0.6 | 0.2 | Multiply | Maximum |
| `Player` | 0.9 | 0.9 | 0.05 | Average | Average |
| `GoalPost` | 0.4 | 0.4 | 0.7 | Average | Maximum |

**Rigidbody Configuration**

| **Object** | **Mass** | **Linear Damping** | **Angular Damping** | **Interpolate** | **Collision Detection** | **Freeze Rotation** |
| --- | --- | --- | --- | --- | --- | --- |
| `Ball` | 0.4 | 0.08 | 2.5 | Interpolate | Continuous Dynamic | N/A |
| `Agent` | 75 | 3 | 6 | Interpolate | Discrete | X and Z |

# 2. Logic and Implementation

This section details the C# scripting architecture that governs individual entities and the overall game flow.

## 2.1 The Physical Agent

This section describes the "body" of the agent - the raw physics and control constraints that define how it interacts with the world before any soccer logic is applied.

### 2.1.1 Soccer Agent's DoF (Degree of Freedom)

The agent operates via a **Multi-Discrete Action Space**. This means that every decision step, the brain outputs a set of integers representing specific "choices" across different categories (Branches).

We use the `Heuristic` method to map player input (Keyboard) to these discrete choices, which allows us to "playtest" the agent's physical constraints manually.

```c#
public override void Heuristic(in ActionBuffers actionsOut)
{
    var d = actionsOut.DiscreteActions;
    
    if (!isControlActive)
    {
        d[0] = 0; d[1] = 0; d[2] = 0;
        return;
    }

    // Branch 0: Translation (WASD) 
    if (Input.GetKey(KeyCode.W)) d[0] = 1;
    else if (Input.GetKey(KeyCode.S)) d[0] = 2;
    else if (Input.GetKey(KeyCode.A)) d[0] = 3;
    else if (Input.GetKey(KeyCode.D)) d[0] = 4;

    // Branch 1: Rotation (Q/E) 
    if (Input.GetKey(KeyCode.Q)) d[1] = 1;
    else if (Input.GetKey(KeyCode.E)) d[1] = 2;

    // Branch 2: Kicking (J/K/L) 
    if (Input.GetKey(KeyCode.J)) d[2] = 1; // Low power
    else if (Input.GetKey(KeyCode.K)) d[2] = 2; // Mid power
    else if (Input.GetKey(KeyCode.L)) d[2] = 3; // High power
}
```

Our agent has **3 DoF**:
1. **Translation X** (Side-to-side)
2. **Translation Z** (Forward-back)
3. **Rotation Y** (Yaw/Turning)

Total Action Space Size: $5 \times 3 \times 4 = $ **60 unique action combinations**  available to the agent at every decision step:
- **Branch 0 (Movement)**: 5 options (Idle, Forward, Backward, Left, Right)
- **Branch 1 (Rotation)**: 3 options (Idle, Clockwise, Counter-Clockwise)
- **Branch 2 (Kicking)**: 4 options (Idle, Low Power, Mid Power, High Poiwer)

While `Heuristic` maps manual keyboard inputs, the `OnActionReceived` method is the "central nervous system" of the agent. It receives the discrete integers (the "choices") and converts them into physical movement through dedicated handler functions. Namely, `Heuristic` is only for human testing; this `OnActionReceived` is what the agent actually uses while training.

```c#
public override void OnActionReceived(ActionBuffers actions)
{
    // Define Side Multiplier: 
    // This allows the same brain to understand "Forward" relative to its own goal.
    // Team A (Red) = 1, Team B (Blue) = -1
    float sideMultiplier = (CachedTeamId == 0) ? 1f : -1f;

    // 1. Locomotion (Branches 0 & 1)
    // Pass movement and rotation indices to the physics handler
    HandleMovement(actions.DiscreteActions[0], actions.DiscreteActions[1], sideMultiplier);
    
    // 2. Kicking (Branch 2)
    // Only execute if the action index is greater than 0 (not Idle)
    int kickPower = actions.DiscreteActions[2];
    if (kickPower > 0) HandleKick(kickPower);
}
```

>Key Logic: The **Side Multiplier** is a critical architectural choice. By multiplying the longitudinal force by this value, we allow the Neural Network to remain agnostic of its world-space position. Whether the agent is on the North or South side of the pitch, a "Move Forward" action always moves it toward the opponent's goal. This significantly reduces training time and allows for seamless Role Swapping during training.

### 2.1.2 Defining the Carry

While basic movement allows the agent to collide with the ball, true "Carrying" ability in our simulation is achieved through a **Damped Spring Attraction Force**. This system simulates a player’s ability to "keep the ball at their feet" by applying subtle magnetic forces that counteract the ball's natural tendency to bounce away.

**Essential Logic: `HandleBallAttraction`**

This logic resides in `FixedUpdate`, running independently of the agent's action choices. It acts as a passive "magnet" whenever the ball is within a specific range and field of view.

```c#
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
```

**Key Components of the Carry Simulation**
- **The Attraction Force**: We apply a force to the ball that pulls it toward a `ControlPoint` located just in front of the agent's "face". This ensures the ball "sticks" during sharp turns.
- **Damping**: To prevent the ball from oscillating wildly or "vibrating" when it reaches the feet, we apply a damping force based on the velocity difference (velError) between the agent and the ball. This "absorbs" the ball's momentum.
- **The FOV Constraint** (minForwardDot): The attraction only works if the ball is in front of the agent. If the agent turns its back to the ball or the ball slips behind, the "magnet" breaks, forcing the agent to turn back around to regain control.
- **The lostControlCooldown**: After a kick or a heavy collision, the attraction is disabled for a short duration (attractDisabledUntil). This prevents the ball from instantly snapping back to the agent's feet after they try to kick it away, allowing for realistic pass and shot trajectories.

>Note on Dribbling Feel: This combination of Intentional Movement (HandleMovement) and Passive Attraction (HandleBallAttraction) creates a "sticky" dribbling feel common in modern soccer games, where the ball stays close during a run but can still be lost if the player is tackled or turns too sharply.

### 2.1.3 Defining the Kick

The kick is the agent's primary tool for scoring and passing. In our system, it is implemented as a high-force, instantaneous burst of energy (Impulse) that originates from a specific "strike zone" in front of the agent.

**Essential Logic: `TheHandleKick`**

The kick logic is gatekept by three factors: the agent's current state, a distance check to the ball, and an internal cooldown.

```c#
private void HandleKick(int powerLevel)
{
    // 1. Pre-checks: State and Cooldowns
    if (_currentState == AgentState.Stalled) return;
    if (Time.time < nextKickAllowedTime) return; // Action cooldown
    if (Time.time < attractDisabledUntil) return; // Cannot kick while recovering control
    
    // 2. Proximity Check: Is the ball in the "Strike Zone"?
    if (Vector3.Distance(controlPoint.position, ballRb.position) < attractRange)
    {
        // 3. Power Mapping
        float impulse = (powerLevel == 1) ? lowKick : (powerLevel == 2) ? midKick : highKick;

        // Exhaustion Penalty: Tired agents kick with 50% less power
        if (isExhausted) impulse *= 0.5f; 

        // 4. State Synchronization
        nextKickAllowedTime = Time.time + kickInternalCooldown;
        attractDisabledUntil = Time.time + lostControlCooldown; // Break the 'Carry' magnet

        // 5. Apply the Physics Impulse
        ballScript.RegisterKick(this); // Tell the ball who hit it for reward tracking
        ballRb.AddForce(transform.forward * impulse, ForceMode.Impulse);
        ballRb.angularVelocity = Vector3.zero; // Clean hit logic

        // Handle Set-Piece transitions
        if (_currentState == AgentState.Armed) SetState(AgentState.Restricted);
    }
}
```

**Key Components of the Kick Mechanic**
- **Force Tiers** (Power Levels): The agent chooses between three power levels from Branch 2 of its action space.
    - Low Kick (1): A precision tap, ideal for short-range passing or "poking" the ball past a defender.
    - Mid Kick (2): A standard pass with balanced power.
    - High Kick (3): A powerful shot/clear. 
- **The "Magnet" Break**: Crucially, a successful kick sets the attractDisabledUntil timer. This disables the Attraction Force (Section 2.1.2) for a short duration (lostControlCooldown). Without this break, the magnetic dribble would instantly pull the ball back to the agent's feet, effectively cancelling out the kick.
- **Clean Hit Logic**: When the kick is applied, we reset the ball's angularVelocity to zero. This ensures a "clean" strike where the trajectory is determined solely by the agent's forward vector, making the result more predictable for the RL model to learn.
- **Exhaustion Scaling**: The kick power is directly linked to the stamina system, which will be introduced in the next subsection. If an agent is in the isExhausted state, their maximum kick impulse is halved. This creates a critical game-loop where an agent who sprints too hard to reach the ball may not have the strength left to make a powerful shot on goal.

### 2.1.4 The Stamina System

To prevent "unlimited sprinting" and force the agent to learn tactical pacing, we implemented a stamina system. Just as a pilot in a dogfight must manage energy to avoid "stalling" during a maneuver, our soccer agents must manage their breath to avoid Exhaustion.

**The Logic of Energy Decay**

Stamina is not just a timer; it is a dynamic pool influenced by the intensity of the agent's physical choices. We track exertion through three primary drains:
- **Linear Speed** (Sprinting): Drain occurs when moving above the sprintThreshold.
- **Angular Speed** (Turning): Drain occurs during high-speed rotation, mimicking the "G-force" energy loss of a sharp aerial maneuver.
- **The Kick** (Action Cost): A flat, immediate stamina penalty applied for high-intensity interactions, which is already mentioned above. 

```c#
private void UpdateStamina()
{
    // 1. Calculate current exertion levels
    Vector3 horizontalVelocity = new Vector3(playerRb.linearVelocity.x, 0, playerRb.linearVelocity.z);
    float linearSpeed = horizontalVelocity.magnitude;
    float rotationThreshold = 10f; 

    float totalDrain = 0f;
    bool isExerting = false;

    // A. Drain from sprinting (high-speed movement)
    if (linearSpeed > sprintThreshold)
    {
        totalDrain += staminaDrainRate;
        isExerting = true;
    }

    // B. Drain from sharp turning (mimicking the G-force of a maneuver)
    if (currentAngularSpeed > rotationThreshold)
    {
        totalDrain += rotationStaminaCost;
        isExerting = true;
    }

    // 2. Apply Change: Deplete if working, Regenerate if resting
    if (isExerting)
    {
        currentStamina -= totalDrain * Time.fixedDeltaTime;
    }
    else
    {
        currentStamina += staminaRegenRate * Time.fixedDeltaTime;
    }

    currentStamina = Mathf.Clamp(currentStamina, 0, maxStamina);

    // 3. Hysteresis Check: Handling the 'Exhausted' state
    if (currentStamina <= 0.1f) isExhausted = true;
    else if (currentStamina > recoveryThreshold) isExhausted = false;
}
```

**The "Stall" Mechanism**: Exhaustion Penalty

When an agent's stamina hits zero, it enters the isExhausted state. This is the soccer equivalent of an airplane losing its airspeed—it becomes sluggish and vulnerable.
- **Movement Penalty**: Inside ApplyFullMovement, the moveSpeed and turnSpeed are multiplied by an exhaustionPenalty (e.g., 0.5). The agent physically cannot keep up with the play.
- **Kick Penalty**: Inside HandleKick, the impulse force is halved. Even if the agent reaches the ball, it lacks the "leg strength" to make a powerful shot.
- **Hysteresis** (The recoveryThreshold): To prevent "jittering" between exhausted and recovered states, the agent must regain a significant amount of stamina (e.g., 20%) before the penalties are lifted. This forces a period of active recovery.

## 2.2 World Rules and Referee

This section covers the Environment Controller. It manages the lifecycle of every episode, enforces the boundaries of the pitch, and orchestrates the complex logic required for professional soccer restarts like corners, goal kicks, and throw-ins.

### 2.2.1 Scene Reset Logic

The Scene Reset logic ensures that every training iteration starts from a scientifically clean state, eliminating any physical or mathematical "noise" from previous plays. This is achieved through a multi-step sanitization process that transitions the world from a terminal state (Goal/Timeout) to a new tactical scenario.


### 2.2.2 The Set Piece System

## 2.3 The RL (Reinforcement Learning) Architecture

### 2.3.1 Observations

### 2.3.2 The Reward Hierarchy

### 2.3.3 Scenario Generation

## 2.4 Training Dynamics


