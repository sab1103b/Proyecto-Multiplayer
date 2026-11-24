using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using Debug = UnityEngine.Debug;

/// <summary>
/// Apartado 1 de la consola: grabar la clase (lo que ve el profesor + su micrófono).
///
/// PERILLA (el modelo de dos bloques):
///   - No se puede mover, solo girar a la derecha o a la izquierda (con la mano o con el rayo).
///   - Al soltarla termina de girar sola hasta el múltiplo de 90° más cercano.
///   - Cuando llega a ese ángulo (no mientras gira) decide:
///       ángulo / 180 da entero  → STOP (de frente se ven los dos bloques)  → pausa la grabación
///       ángulo / 180 da decimal → REC  (de lado se ve un solo cuadrado)     → graba
///
/// PANTALLA: tiempo grabado y botón "Guardar video" (se habilita en STOP).
///   Volver a REC antes de guardar continúa el mismo video.
///
/// GRABACIÓN (solo Windows): una cámara extra copia la vista de la cabeza del profesor y cada cuadro
/// se manda a ffmpeg.exe (en StreamingAssets/ffmpeg/), que arma el .mp4. El micrófono se guarda en un
/// .wav y al final ffmpeg une video + audio. El archivo queda en Videos/Cubo VR.
/// </summary>
public class GrabadoraClase : MonoBehaviour
{
    [Header("Perilla STOP / REC")]
    public XRSimpleInteractable perilla;
    [Tooltip("Grados por segundo con los que termina de girar sola al soltarla.")]
    public float velocidadAjuste = 300f;
    [Tooltip("Con el rayo se gira la muñeca: este número multiplica ese giro.")]
    public float gananciaRayo = 2f;

    [Header("Pantalla")]
    public GameObject pantalla;
    public Text textoTiempo;
    public Text textoEstado;
    public Button botonGuardar;
    public Graphic puntoRec;

    [Header("Video")]
    public int ancho = 1280;
    public int alto = 720;
    public int cuadrosPorSegundo = 30;
    public float campoDeVision = 90f;

    enum Estado { SinGrabar, Grabando, Pausado, Guardando }
    Estado estado = Estado.SinGrabar;

    // ---- Perilla ----
    float angulo;            // ángulo actual (grados)
    float anguloDestino;
    bool acomodando;         // terminando de girar sola
    IXRSelectInteractor manoQueGira;
    float anguloManoAlAgarrar, anguloPerillaAlAgarrar;

    // ---- Grabación ----
    Camera camaraGrabacion;
    RenderTexture imagen;
    Process ffmpeg;
    Thread hiloEscritura;
    BlockingCollection<byte[]> cuadrosPendientes;
    bool esperandoCuadro;
    int cuadrosEnviados;
    float segundosGrabados;
    string carpetaTemporal, rutaVideoTemp, rutaAudioTemp;

    // ---- Micrófono ----
    AudioClip micro;
    int ultimaPosicionMicro;
    FileStream archivoAudio;
    int muestrasAudio;
    int canalesAudio = 1, frecuenciaAudio = 48000;

    void Awake()
    {
        if (pantalla != null) pantalla.SetActive(false);
        if (botonGuardar != null) botonGuardar.onClick.AddListener(Guardar);
        if (perilla != null)
        {
            angulo = perilla.transform.localEulerAngles.y;
            perilla.selectEntered.AddListener(AlAgarrar);
            perilla.selectExited.AddListener(AlSoltar);
        }
    }

    void Update()
    {
        GirarPerilla();
        if (estado == Estado.Grabando || estado == Estado.Pausado) LeerMicrofono(estado == Estado.Grabando);
        if (estado == Estado.Grabando)
        {
            segundosGrabados += Time.unscaledDeltaTime;
            PedirCuadro();
        }
        ActualizarPantalla();
    }

    // =====================================================================
    // 1. PERILLA
    // =====================================================================
    void AlAgarrar(SelectEnterEventArgs args)
    {
        if (!ConsolaProfesor.EsProfesorLocal()) return;
        manoQueGira = args.interactorObject;
        anguloManoAlAgarrar = AnguloDeLaMano(manoQueGira);
        anguloPerillaAlAgarrar = angulo;
        acomodando = false;
    }

    void AlSoltar(SelectExitEventArgs args)
    {
        if (args.interactorObject != manoQueGira) return;
        manoQueGira = null;
        anguloDestino = Mathf.Round(angulo / 90f) * 90f; // el múltiplo de 90° más cercano
        acomodando = true;
    }

    void GirarPerilla()
    {
        if (perilla == null) return;

        if (manoQueGira != null)
        {
            float giro = Mathf.DeltaAngle(anguloManoAlAgarrar, AnguloDeLaMano(manoQueGira));
            if (manoQueGira is XRRayInteractor) giro *= gananciaRayo;
            angulo = anguloPerillaAlAgarrar + giro;
        }
        else if (acomodando)
        {
            angulo = Mathf.MoveTowards(angulo, anguloDestino, velocidadAjuste * Time.deltaTime);
            if (Mathf.Approximately(angulo, anguloDestino))
            {
                acomodando = false;
                angulo = Mathf.Repeat(anguloDestino, 360f);
                AlLlegarAlAngulo(angulo); // la acción ocurre solo al quedar quieta
            }
        }
        perilla.transform.localRotation = Quaternion.Euler(0f, angulo, 0f);
    }

    /// <summary>
    /// Con la mano: hacia dónde está la mano alrededor de la perilla (vista desde arriba).
    /// Con el rayo: hacia dónde apunta el control (girar la muñeca).
    /// </summary>
    float AnguloDeLaMano(IXRSelectInteractor mano)
    {
        Transform consola = perilla.transform.parent != null ? perilla.transform.parent : perilla.transform;
        if (mano is XRRayInteractor)
        {
            Vector3 f = consola.InverseTransformDirection(mano.transform.forward);
            return Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
        }
        Vector3 d = consola.InverseTransformPoint(mano.GetAttachTransform(perilla).position)
                    - perilla.transform.localPosition;
        return Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
    }

    void AlLlegarAlAngulo(float grados)
    {
        // Regla: STOP = 0°, si grados / 180 da un número entero está detenida; si da decimal, graba.
        float division = grados / 180f;
        bool esEntero = Mathf.Approximately(division, Mathf.Round(division));
        if (esEntero) Pausar();
        else Grabar();
    }

    // =====================================================================
    // 2. GRABAR / PAUSAR / GUARDAR
    // =====================================================================
    void Grabar()
    {
        if (estado == Estado.Guardando) return;
        if (pantalla != null) pantalla.SetActive(true);
        if (estado == Estado.SinGrabar && !Empezar()) return;
        estado = Estado.Grabando;
        if (camaraGrabacion != null) camaraGrabacion.enabled = true;
        Mensaje("Grabando");
    }

    void Pausar()
    {
        if (estado != Estado.Grabando) return;
        estado = Estado.Pausado;
        if (camaraGrabacion != null) camaraGrabacion.enabled = false;
        Mensaje("Detenido. Guarda el video o gira a REC para seguir.");
    }

    void Guardar()
    {
        if (estado != Estado.Pausado) return;
        estado = Estado.Guardando;
        StartCoroutine(Finalizar());
    }

    bool Empezar()
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        string exe = RutaFfmpeg();
        if (exe == null)
        {
            Mensaje("Falta ffmpeg.exe en Assets/StreamingAssets/ffmpeg/");
            return false;
        }

        carpetaTemporal = Path.Combine(Application.temporaryCachePath, "Grabacion");
        Directory.CreateDirectory(carpetaTemporal);
        rutaVideoTemp = Path.Combine(carpetaTemporal, "video.mp4");
        rutaAudioTemp = Path.Combine(carpetaTemporal, "audio.wav");

        // Cámara que copia la vista del profesor (sin estéreo) y dibuja en una textura.
        var cabeza = VRRigReferences.Instance != null && VRRigReferences.Instance.head != null
            ? VRRigReferences.Instance.head
            : (Camera.main != null ? Camera.main.transform : null);
        if (cabeza == null) { Mensaje("No encontré la cámara del profesor."); return false; }

        imagen = new RenderTexture(ancho, alto, 24, RenderTextureFormat.ARGB32);
        var go = new GameObject("Camara_Grabacion");
        go.transform.SetParent(cabeza, false);
        camaraGrabacion = go.AddComponent<Camera>();
        if (Camera.main != null) camaraGrabacion.cullingMask = Camera.main.cullingMask;
        camaraGrabacion.stereoTargetEye = StereoTargetEyeMask.None;
        camaraGrabacion.fieldOfView = campoDeVision;
        camaraGrabacion.nearClipPlane = 0.02f;
        camaraGrabacion.targetTexture = imagen;
        camaraGrabacion.enabled = false;

        // ffmpeg recibe los cuadros crudos por su entrada estándar y los comprime a MP4.
        string args = $"-y -loglevel error -f rawvideo -pix_fmt rgba -s {ancho}x{alto} -r {cuadrosPorSegundo} -i - " +
                      $"-vf vflip -c:v libx264 -preset ultrafast -crf 23 -pix_fmt yuv420p \"{rutaVideoTemp}\"";
        try
        {
            ffmpeg = Process.Start(new ProcessStartInfo(exe, args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardError = true,
            });
            ffmpeg.ErrorDataReceived += (_, e) => { if (!string.IsNullOrEmpty(e.Data)) Debug.LogWarning("[ffmpeg] " + e.Data); };
            ffmpeg.BeginErrorReadLine();
        }
        catch (Exception e)
        {
            Mensaje("No pude abrir ffmpeg: " + e.Message);
            Limpiar();
            return false;
        }

        cuadrosPendientes = new BlockingCollection<byte[]>(90);
        var entrada = ffmpeg.StandardInput.BaseStream;
        var cola = cuadrosPendientes;
        hiloEscritura = new Thread(() =>
        {
            try { foreach (var cuadro in cola.GetConsumingEnumerable()) entrada.Write(cuadro, 0, cuadro.Length); }
            catch (Exception e) { Debug.LogWarning("[CuboVR] Error escribiendo video: " + e.Message); }
        }) { IsBackground = true, Name = "Grabacion video" };
        hiloEscritura.Start();

        EmpezarMicrofono();
        segundosGrabados = 0f;
        cuadrosEnviados = 0;
        esperandoCuadro = false;
        return true;
#else
        Mensaje("La grabación solo funciona en Windows.");
        return false;
#endif
    }

    // Se pide la imagen a la GPU sin congelar el juego; llega unos cuadros después.
    void PedirCuadro()
    {
        if (esperandoCuadro || imagen == null) return;
        esperandoCuadro = true;
        AsyncGPUReadback.Request(imagen, 0, TextureFormat.RGBA32, AlLlegarCuadro);
    }

    void AlLlegarCuadro(AsyncGPUReadbackRequest pedido)
    {
        esperandoCuadro = false;
        if (pedido.hasError || cuadrosPendientes == null || cuadrosPendientes.IsAddingCompleted) return;
        byte[] datos = pedido.GetData<byte>().ToArray();

        // Para que el video dure lo mismo que el audio: se mandan tantos cuadros como "tocan" según el tiempo.
        int debidos = Mathf.FloorToInt(segundosGrabados * cuadrosPorSegundo) - cuadrosEnviados;
        for (int i = 0; i < debidos; i++)
        {
            if (!cuadrosPendientes.TryAdd(datos, 50)) break;
            cuadrosEnviados++;
        }
    }

    IEnumerator Finalizar()
    {
        Mensaje("Guardando video...");
        TerminarMicrofono();
        cuadrosPendientes.CompleteAdding();

        string carpetaFinal = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "Cubo VR");
        string rutaFinal = Path.Combine(carpetaFinal, $"Clase_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.mp4");
        string exe = RutaFfmpeg();
        bool hayAudio = muestrasAudio > 0;
        var proceso = ffmpeg;
        var hilo = hiloEscritura;
        string video = rutaVideoTemp, audio = rutaAudioTemp;

        // Lo pesado va en otro hilo para no congelar la escena.
        var tarea = Task.Run(() =>
        {
            hilo.Join();
            proceso.StandardInput.Close();
            proceso.WaitForExit();
            Directory.CreateDirectory(carpetaFinal);
            if (hayAudio)
            {
                var unir = Process.Start(new ProcessStartInfo(exe,
                    $"-y -loglevel error -i \"{video}\" -i \"{audio}\" -c:v copy -c:a aac -b:a 160k -shortest \"{rutaFinal}\"")
                { UseShellExecute = false, CreateNoWindow = true });
                unir.WaitForExit();
            }
            else File.Copy(video, rutaFinal, true);
        });
        while (!tarea.IsCompleted) yield return null;

        if (tarea.IsFaulted || !File.Exists(rutaFinal))
        {
            Mensaje("No se pudo guardar el video (mira la consola).");
            if (tarea.Exception != null) Debug.LogException(tarea.Exception);
        }
        else
        {
            Mensaje("Guardado en Videos/Cubo VR");
            Debug.Log("[CuboVR] Video guardado: " + rutaFinal);
        }
        Limpiar();
        estado = Estado.SinGrabar;
        yield return new WaitForSeconds(4f);
        if (estado == Estado.SinGrabar && pantalla != null) pantalla.SetActive(false);
    }

    void Limpiar()
    {
        if (camaraGrabacion != null) Destroy(camaraGrabacion.gameObject);
        if (imagen != null) { imagen.Release(); Destroy(imagen); }
        camaraGrabacion = null;
        imagen = null;
        ffmpeg = null;
        hiloEscritura = null;
        cuadrosPendientes = null;
    }

    void OnDestroy()
    {
        // Si se cierra la app grabando, se corta ffmpeg para no dejarlo abierto.
        if (estado == Estado.SinGrabar) return;
        try { cuadrosPendientes?.CompleteAdding(); } catch { }
        try { if (ffmpeg != null && !ffmpeg.HasExited) ffmpeg.Kill(); } catch { }
        TerminarMicrofono();
    }

    static string RutaFfmpeg()
    {
        string enProyecto = Path.Combine(Application.streamingAssetsPath, "ffmpeg", "ffmpeg.exe");
        return File.Exists(enProyecto) ? enProyecto : null;
    }

    // =====================================================================
    // 3. MICRÓFONO → archivo .wav
    // =====================================================================
    void EmpezarMicrofono()
    {
        muestrasAudio = 0;
        if (Microphone.devices.Length == 0)
        {
            Debug.LogWarning("[CuboVR] No hay micrófono: el video se guardará sin sonido.");
            return;
        }
        Microphone.GetDeviceCaps(null, out int min, out int max);
        frecuenciaAudio = max == 0 ? 48000 : Mathf.Clamp(48000, min, max);
        micro = Microphone.Start(null, true, 2, frecuenciaAudio);
        canalesAudio = micro.channels;
        ultimaPosicionMicro = 0;
        archivoAudio = new FileStream(rutaAudioTemp, FileMode.Create);
        archivoAudio.Write(new byte[44], 0, 44); // la cabecera se escribe al final
    }

    void LeerMicrofono(bool guardar)
    {
        if (micro == null || archivoAudio == null) return;
        int posicion = Microphone.GetPosition(null);
        if (posicion == ultimaPosicionMicro) return;

        int disponibles = posicion > ultimaPosicionMicro ? posicion - ultimaPosicionMicro : micro.samples - ultimaPosicionMicro + posicion;
        if (guardar)
        {
            var muestras = new float[disponibles * canalesAudio];
            micro.GetData(muestras, ultimaPosicionMicro); // da la vuelta sola al final del clip
            var bytes = new byte[muestras.Length * 2];
            for (int i = 0; i < muestras.Length; i++)
            {
                short s = (short)(Mathf.Clamp(muestras[i], -1f, 1f) * short.MaxValue);
                bytes[i * 2] = (byte)s;
                bytes[i * 2 + 1] = (byte)(s >> 8);
            }
            archivoAudio.Write(bytes, 0, bytes.Length);
            muestrasAudio += muestras.Length;
        }
        ultimaPosicionMicro = posicion;
    }

    void TerminarMicrofono()
    {
        if (micro != null)
        {
            LeerMicrofono(estado == Estado.Grabando);
            Microphone.End(null);
            micro = null;
        }
        if (archivoAudio == null) return;

        // Cabecera WAV (PCM de 16 bits).
        int bytesDatos = muestrasAudio * 2;
        archivoAudio.Seek(0, SeekOrigin.Begin);
        var w = new BinaryWriter(archivoAudio);
        w.Write(new[] { 'R', 'I', 'F', 'F' }); w.Write(36 + bytesDatos);
        w.Write(new[] { 'W', 'A', 'V', 'E' }); w.Write(new[] { 'f', 'm', 't', ' ' });
        w.Write(16); w.Write((short)1); w.Write((short)canalesAudio); w.Write(frecuenciaAudio);
        w.Write(frecuenciaAudio * canalesAudio * 2); w.Write((short)(canalesAudio * 2)); w.Write((short)16);
        w.Write(new[] { 'd', 'a', 't', 'a' }); w.Write(bytesDatos);
        w.Flush();
        archivoAudio.Dispose();
        archivoAudio = null;
    }

    // =====================================================================
    // 4. PANTALLA
    // =====================================================================
    void ActualizarPantalla()
    {
        if (pantalla == null || !pantalla.activeSelf) return;
        if (textoTiempo != null)
        {
            int s = Mathf.FloorToInt(segundosGrabados);
            textoTiempo.text = $"{s / 60:00}:{s % 60:00}";
        }
        if (puntoRec != null)
            puntoRec.enabled = estado == Estado.Grabando ? Mathf.Repeat(Time.time, 1f) < 0.6f : estado == Estado.Pausado;
        if (botonGuardar != null) botonGuardar.interactable = estado == Estado.Pausado;
    }

    void Mensaje(string texto)
    {
        if (textoEstado != null) textoEstado.text = texto;
        if (pantalla != null) pantalla.SetActive(true);
    }
}
