using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace CuboVR.Networking
{
    /// <summary>
    /// Solo en el build de Windows: al abrir la app revisa si el Firewall de Windows ya permite
    /// esta aplicación. Si no, pide UNA vez permiso de administrador (ventana de Windows "¿Permitir que
    /// esta aplicación haga cambios?") y crea la regla. Así no hay que configurar nada a mano en cada PC.
    ///
    /// La regla permite conexiones ENTRANTES solo para este .exe y solo desde la red local
    /// (remoteip=localsubnet), en cualquier tipo de red (Privada o Pública), así sirve tanto
    /// para crear salas (profesor) como para encontrarlas (estudiante).
    /// Si el usuario dice "No", la app sigue funcionando y lo vuelve a preguntar en el próximo inicio.
    /// </summary>
    public static class FirewallSetup
    {
        public const string RuleName = "Cubo VR (red local)";

        /// <summary>null = revisando, true = permitido, false = sin regla (el usuario no aceptó o falló).</summary>
        public static bool? Allowed { get; private set; }

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoRun()
        {
            string exe = GetExePath();
            if (string.IsNullOrEmpty(exe)) return;
            new Thread(() => EnsureRule(exe)) { IsBackground = true, Name = "CuboVR-Firewall" }.Start();
        }
#endif

        static string GetExePath()
        {
            try
            {
                string path = Environment.GetCommandLineArgs()[0];
                path = Path.GetFullPath(path);
                return File.Exists(path) ? path : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        static void EnsureRule(string exe)
        {
            try
            {
                if (RuleExistsFor(exe))
                {
                    Allowed = true;
                    Debug.Log("[CuboVR] Firewall: la app ya tiene permiso en la red local.");
                    return;
                }

                Debug.Log("[CuboVR] Firewall: pidiendo permiso de administrador para crear la regla…");
                string args = $"advfirewall firewall add rule name=\"{RuleName}\" dir=in action=allow " +
                              $"program=\"{exe}\" enable=yes profile=any remoteip=localsubnet";
                var psi = new ProcessStartInfo("netsh", args)
                {
                    UseShellExecute = true,   // necesario para "runas" (pedir administrador)
                    Verb = "runas",
                    WindowStyle = ProcessWindowStyle.Hidden,
                };
                using (var p = Process.Start(psi))
                {
                    p?.WaitForExit(60000);
                }

                Allowed = RuleExistsFor(exe);
                if (Allowed == true) Debug.Log("[CuboVR] Firewall: regla creada correctamente.");
                else Debug.LogWarning("[CuboVR] Firewall: no se creó la regla. Puede que otros no encuentren o no se conecten a tu sala.");
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // El usuario pulsó "No" en la ventana de permisos.
                Allowed = false;
                Debug.LogWarning("[CuboVR] Firewall: se canceló el permiso de administrador. Se volverá a preguntar la próxima vez.");
            }
            catch (Exception e)
            {
                Allowed = false;
                Debug.LogWarning($"[CuboVR] Firewall: no se pudo revisar/crear la regla: {e.Message}");
            }
        }

        /// <summary>Consulta (sin permisos de administrador) si existe la regla para esta ruta exacta del .exe.
        /// Si la carpeta del juego se movió, la ruta cambia y se vuelve a pedir permiso.</summary>
        static bool RuleExistsFor(string exe)
        {
            var psi = new ProcessStartInfo("netsh", $"advfirewall firewall show rule name=\"{RuleName}\" verbose")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using (var p = Process.Start(psi))
            {
                if (p == null) return false;
                string output = p.StandardOutput.ReadToEnd();
                p.WaitForExit(10000);
                if (p.ExitCode != 0) return false;
                return output.IndexOf(exe, StringComparison.OrdinalIgnoreCase) >= 0;
            }
        }
    }
}
