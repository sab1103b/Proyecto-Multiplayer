namespace CuboVR.Networking
{
    /// <summary>Rol de cada jugador dentro de una sala. Solo hay un Profesor por sala: el host.</summary>
    public enum PlayerRole : byte
    {
        Ninguno = 0,
        Profesor = 1,
        Estudiante = 2,
    }
}
