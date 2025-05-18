using UnityEngine;

public class EnumData : MonoBehaviour
{

    [System.Serializable]
    public enum Direction : byte
    {
        None = 0,
        Left = 1,
        Right = 2,
        Bottom = 3,
        Top = 4,
        Back = 5,
        Front = 6
    }

    [System.Serializable]
    public enum BlocksID : byte
    {
        Air = 0,
        Grass = 1,
        Dirt = 2,
        Stone = 3
    }

}
