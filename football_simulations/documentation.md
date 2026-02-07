# Introduction

This project serves as a foundation for Multi-Agent Reinforcement Learning (MARL) within a soccer environment. The core design philosophy is the abstraction of physical complexity in favor of tactical decision-making, thus providing a clear signal for agents to learn higher-level coordination and strategy.

# 1. The Physical Environment

The environment is built with a hierarchical structure in Unity, rooted at the `EnvRoot` object.

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

## 1.1 The Play Area

The `Pitch` group defines the Play Area: the **physical boundaries** and **surface layout** where the soccer agents and the ball are contained. 

- `Ground`: A static plane with a scale of 4:1:7, providing a total physical surface of 40m x 70m. 
- `Wall`: Physical barriers positioned slightly outside the visible pitch boundaries. They prevent agents and the ball from falling off the ground into the void.
- `GoalTrigger`: A collision-detecting volume located behind the goal line. Contact between the ball and this trigger serves as the final success state for an episode.
- `GoalPost`: Each goal is composed of two collidable cylinders. Since the current simulation excludes lofted kicks, a horizontal crossbar is omitted. These cylinders are physically active to simulate realistic "bounce-back" behavior when the ball strikes the post.
- `GoalArea`: Green rectangles placed at either end of the field. These act as clear indicators for the user to identify the scoring zones. They do not contain any trigger logic.
- `FieldLine`: Visual markers placed on the ground to indicate the standard boundaries of a soccer pitch.
- `OOBRoot`: The logical boundaries of the match. This system uses a series of intangible trigger volumes (OOB_E, OOB_W, etc.) to manage game state transitions.

The **"Full Clearance" Boundary Logic**: OOB triggers are placed exactly at the perimeter of the 40m x 70m ground. When the reset is triggered, the ball's innermost edge is 1.2m away from the trigger. This effectively creates a playing field of 37.6m x 67.6m (40−1.2×2 for width, 70−1.2×2 for length).

Visual Alignment: The FieldLines are placed slightly inward from the physical walls/OOB triggers. This ensures that when the user sees the ball completely cross the white line, the physics engine simultaneously registers the OOB hit, making the visual experience and the logic synchronous.

![OOB_vs_Wall](./media/oob_vs_wall.png)

<p align="center"><i>Fig 1. A visual comparison between OOB and Wall objects</i></p>

## 1.2 The Ball and Agents

This section defines the dynamic entities that interact within the Play Area.

- `Ball`: The central objective of the game. Its *1.2m* diameter, combined with the placement of OOB triggers at the *40m x 70m* mark, creates an effective playing field of *37.6m x 67.6m* based on the "Full Clearance" rule.
- `Agent`: Decision-making entity (player) represented as Cubes. They are divided into *Team A* and *Team B*, each with a specific goal orientation.

## 1.3 Physical Properties and Simulation Constants

### 1.3.1 Physic Materials

| **Object** | **Dynamic Friction** | **Static Friction** | **Bounciness** | **Friction Combine** | **Bounce Combine** |
| --- | --- | --- | --- | --- | --- |
| `Ground` | 0.8 | 0.8 | 0 | Multiply | Minimum |
| `Ball` | 0.6 | 0.6 | 0.2 | Multiply | Maximum |
| `Player` | 0.9 | 0.9 | 0.05 | Average | Average |
| `GoalPost` | 0.4 | 0.4 | 0.7 | Average | Maximum |

### 1.3.2 Rigidbody Settings

| **Object** | **Mass** | **Linear Damping** | **Angular Damping** | **Interpolate** | **Collision Detection** | **Freeze Rotation** |
| --- | --- | --- | --- | --- | --- | --- |
| `Ball` | 0.4 | 0.08 | 2.5 | Interpolate | Continuous Dynamic | N/A |
| `Agent` | 75 | 3 | 6 | Interpolate | Discrete | X and Z |

# Logic and Implementation

This section details the C# scripting architecture that governs individual entities and the overall game flow.

