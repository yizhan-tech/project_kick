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

## 1.2 Dynamic Entities

The simulation tracks two primary dynamic classes: the Ball and the Agents. These are the only objects utilizing active `Rigidbody` components.

### 1.2.1 The Ball

The central objective of the simulation.
- **Scale**: 1.2m Diameter.
- **Physics Role**: Acts as the primary target for agent interaction. Its high bounciness against goalposts and low linear damping are tuned to produce long, predictable trajectories.

### 1.2.2 The Agents

Active entities represented as 1.2m x 1.2m x 1.2m cubes.
- **Team Orientation**: Agents are categorized into `Team A` (Red) and `Team B` (Blue).
- **The Control Point**: A dedicated `Transform` located at the front-base of the agent. This represents the "Sweet Spot" for physical interaction, used as the origin for both dribbling forces and kicking impulses.

## 1.3 Physic Materials and Constants

Deterministic behavior is maintained through specific `PhysicMaterial` configurations, defining surface friction and energy restitution (Bounciness).

### 1.3.1 Material Properties

| **Object** | **Friction (D/S)** | **Bounciness** | **Friction/Bounce Combine** |
| --- | --- | --- | --- |
| `Ground` | 0.8 / 0.8 | 0.0 | Multiply / Minimum |
| `Ball` | 0.6 / 0.6 | 0.2 | Multiply / Maximum |
| `Player` | 0.9 / 0.9 | 0.05 | Average / Average |
| `GoalPost` | 0.4 / 0.4 | 0.7 | Average / Maximum |

### 1.3.2 Rigidbody Settings

| **Object** | **Mass** | **Linear/Angular Damping** | **Interpolate** | **Collision Detection** | **Freeze Rotation** |
| --- | --- | --- | --- | --- | --- |
| `Ball` | 0.4 | 0.08 / 2.5 | Interpolate | Continuous Dynamic | N/A |
| `Agent` | 75.0 | 3.0 / 6.0 | Interpolate | Discrete | X and Z |

# 2. Logic and Implementation

This chapter details the underlying software architecture that governs individual entities and the macro-level game loop. These systems are designed to provide a consistent, physically-grounded simulation of soccer.

## 2.1 The Agent Controller

The `AgentController` is the primary interface between the game logic and the physical world. It translates abstract intent into specialized force applications.

### 2.1.1 Locomotion and Degrees of Freedom (DoF)

Each agent is constrained to a 3-DoF movement model, optimized for ground-level maneuvers:
- **Translation (X & Z Axis)**: Agents apply linear force to their Rigidbody to navigate the pitch.
- **Rotation (Y Axis)**: High-torque angular forces allow agents to pivot and orient themselves toward the ball or goals.

To ensure consistency across the field, movement is calculated using a **Relative Side Multiplier**. Whether an agent is assigned to the North or South side, a "Forward" command always translates into force directed toward the opposing goal.

### 2.1.2 The "Carry" (Ball Attraction)

To simulate the nuanced control of a player's feet, we utilize a Damped Spring Attraction Force.
- **The Sweet Spot**: A `ControlPoint` transform located at the agent's base acts as a magnetic anchor.
- **Damping**: To prevent the ball from oscillating or vibrating upon contact, a counter-force is applied based on the velocity difference between the agent and the ball, "absorbing" the ball's momentum into the agent's stride.

### 2.1.3 Striking Mechanics (The Kick)

Kicking is implemented as a discrete **Physical Impulse** applied to the ball.
- **Zone Gating**: The kick is only executable if the ball enters the agent's "Strike Zone".
- **Impulse Tiers**: The system supports 3 distinct power levels (Low, Mid, and High), allowing for a range of tactical outputs from short precision passes to high-velocity shots.
- **The Attraction Break**: Upon execution, the "Carry" attraction is temporarily disabled. This ensures the ball can travel along its intended trajectory without being immediately re-captured by the agent's magnetic "feet."

## 2.2 High-Intensity Stamina System

Unlike traditional sports simulations that track fatigue over a 90-minute match, our system focuses on **Short-Term Anaerobic Exertion**. This simulates the explosive bursts of energy required for sprinting, sharp cutting, and powerful striking.

### 2.2.1 Energy Consumption (The Drain)

Stamina is a dynamic pool (0 - 100%) that depletes rapidly under high-intensity actions:
- **Sprinting**: Moving above a specific velocity threshold triggers a linear drain.
- **High-Torque Turning**: Applying maximum angular force to pivot at speed incurs a "maneuvering cost," simulating the physical tax of maintaining balance during sharp turns.
- **Action Bursts**: Executing a high-power kick applies an immediate "snapshot" penalty to the stamina pool.

### 2.2.2 The Exhaustion State

When the stamina pool is depleted (<0.1%), the agent enters a **Stall Condition (Exhausted)**.
- **Performance Scaling**: Movement and rotation speeds are globally throttled by 50%.
- **Reduced Output**: Kick impulses are significantly weakened, preventing powerful shots while tired.
- **Recovery Hysteresis**: To prevent "flickering" between states, an agent must recover to a 20% threshold before full mobility and power are restored, forcing tactical "rest" periods.

## 2.3 Environment Controller (Game Cycle)

The `EnvController` acts as the "Referee" and "Match Engine," managing the progression of the match and enforcing the rules of play.

### 2.3.1 The Game Loop and Playback Phase

The match operates in a continuous cycle, monitored every physics step. The controller tracks the GamePhase (who has possession) and PlayType (Open Play vs. Set Piece).
- **Stall Timers**: When play is interrupted (e.g., a ball goes out of bounds), the controller initiates a "Stall" phase, freezing the state to allow for scenario reconfiguration.
- **Scenario Loading**: The system utilizes a library of tactical setups (Kick-offs, 1v1 drills, etc.) to populate the field. This ensures variety in game-start conditions.

### 2.3.2 Boundary and Goal Resolution

The controller monitors global trigger events to manage match flow:
- **Goal Resolution**: Upon a GoalTrigger event, the controller assigns credit to the scoring team and initiates a full scene reset.
- **OutOfBounds (OOB) Handling**: If the ball contacts an OOB volume, the controller determines the restart type (Goal Kick, Corner, or Throw-in) based on the last agent to contact the ball and the location of the exit.

### 2.3.3 Set-Piece Management

Restarts are governed by a Set-Piece Taker system:
- **Selection**: The closest player on the beneficiary team is assigned as the "Taker."
- **Positioning**: The ball is placed at the restart point, and the Taker is teleported to a "Ready" position.
- **Clearance Zone**: All other agents are physically pushed back to a 5m radius to ensure the Taker has a clear path for the restart.
- **Resumption**: Once the stall timer expires, the Taker is "Armed," and play resumes.






















## 2.3 The RL (Reinforcement Learning) Architecture

### 2.3.1 Observations

### 2.3.2 The Reward Hierarchy

### 2.3.3 Scenario Generation

## 2.4 Training Dynamics


