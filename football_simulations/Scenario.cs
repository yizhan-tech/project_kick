using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "NewScenario", menuName = "Soccer/Scenario")]
public class Scenario : ScriptableObject
{
    public enum SpawnMode { Zone, AnchorTarget, Direct }
    public enum AnchorPoint { Ball, OwnGoal, OppGoal, MidfieldCenter }
    
    [System.Flags]
    public enum FieldZone 
    { 
        None = 0,
        DefLeft = 1, DefCenter = 2, DefRight = 4,
        MidLeft = 8, MidCenter = 16, MidRight = 32,
        AttLeft = 64, AttCenter = 128, AttRight = 256,
        All = 511
    }

    [Header("Ball Setup")]
    public SpawnMode ballSpawnMode = SpawnMode.Zone;
    public FieldZone ballZones = FieldZone.MidCenter;
    public Vector3 ballDirectPos;
    public float ballJitter = 1f;

    [Header("Team Mirroring")]
    public bool zMirrorForTeamB = true;

    [System.Serializable]
    public struct AgentSetup
    {
        public bool isActive;
        public SpawnMode mode;
        
        // Define the enum type first (no attributes here)
        public enum LookAtTarget { Ball, OpponentGoal, OwnGoal, Custom }

        [Header("Mode A: Zone")]
        public FieldZone zones;

        [Header("Mode B: Anchor-Target")]
        public AnchorPoint anchor;
        public AnchorPoint target;
        public bool extendOutward; 
        public float minDistance;
        public float maxDistance; 
        public float orthogonalOffset; 

        [Header("Mode C: Direct")]
        public Vector3 directPos;

        [Header("Orientation")]
        public LookAtTarget lookAt; // The attribute goes on this variable
        [Range(0, 180)]
        public float rotationRandomness; 
        public Vector3 rotation; // Used if lookAt is Custom

        [Header("Jitter")]
        public float jitter;
    }

    public List<AgentSetup> teamA;
    public List<AgentSetup> teamB;
}