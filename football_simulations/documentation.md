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

## 1.1 The Pitch

The `Pitch` group defines the Play Area: the **physical boundaries** and **surface layout** where the soccer agents and the ball are contained. It establishes the foundational space where physical movement is possible, separate from the *scoring* and *rule-based systems*.

- `Ground`: A static plane with a scale of 4:1:7, providing a total physical surface of 40m x 70m. The mesh uses a custom **Physic Material** (*Assets/Materials/Physics/PitchPhysics*) to ensure consistent ball-handling and locomotion.
    - **Friction**: High friction values are set to prevent the ball from sliding indefinitely, forcing agents to actively "push" or "carry" the ball to maintain momentum.
    - **Bounciness**: Set to zero to minimize vertical jitter and keep the ball grounded, focusing the simulation on 2D tactical play.
    - **Combine Modes**: Friction is set to Multiply to emphasize the interaction between the ball and the playing surface, while Bounciness is set to Minimum to suppress any unintended elastic energy from the ground surface.
- `FieldLine`: Visual markers placed on the ground to indicate the standard boundaries of a soccer pitch.
- `Wall`: Physical barriers positioned slightly outside the visible pitch boundaries. They prevent agents and the ball from falling off the ground into the void while allowing agents to move slightly beyond the "Play Area" lines.


## 1.2 Out of Bounds (OOB) System

While the physical walls provide a mechanical boundary to keep the ball and agents contained, the `OOBRoot` defines the logical boundaries of the match. This system uses a series of intangible trigger volumes (OOB_E, OOB_W, etc.) to manage game state transitions.

## The Goal Structures
- `GoalArea`: green rectangles placed at either end of the field. These act as clear indicators for the user to identify the scoring zones, as the physical goalposts are thin. They do not contain any trigger logic.
- `GoalPost`: Each goal is composed of two collidable cylinders. Since the current simulation excludes lofted kicks, a horizontal crossbar is omitted. These cylinders are physically active to simulate realistic "bounce-back" behavior when the ball strikes the post.
- `GoalTrigger`: A collision-detecting volume located behind the goal line. Contact between the ball and this trigger serves as the final success state for an episode. When triggered, the environment awards a global reward to the scoring team and initiates a scene reset.