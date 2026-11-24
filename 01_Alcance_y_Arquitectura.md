# 01. ALCANCE Y ARQUITECTURA DEL PRODUCTO: CUBO VR

## 1. Definición General del Producto
**Cubo VR (Presentaciones dentro de un cubo)** es un entorno tridimensional multiusuario de Realidad Virtual diseñado para presentaciones educativas inmersivas no lineales (desarrollado bajo los lineamientos del 3DUI Contest de IEEE VR 2026: *"Beyond Slides"*).

El sistema reemplaza las diapositivas secuenciales 2D por un **cubo transparente de cuatro superficies laterales**:
- **3 Caras Principales de Presentación:** Lienzos interactivos donde el presentador escribe, dibuja, inserta objetos 3D temáticos y asocia audio sin orden obligatorio.
- **1 Cara Auxiliar de Control:** Superficie dedicada exclusivamente a la navegación, limpieza de lienzos, exportación de capturas y generación de recursos 3D, manteniendo limpias las áreas de exposición.

## 2. Plataforma y Restricciones Técnicas
- **Plataforma Objetivo:** Visores de Realidad Virtual compatibles con el estándar **OpenXR** (y soporte de pruebas mediante XR Device Simulator en Unity).
- **Exclusiones Explícitas / Trabajo Futuro:**
  - Exportación de audio espacializado embebido por superficie junto con el paquete 3D queda diferido como trabajo futuro (actualmente se exportan imágenes PNG por cara y grabación POV del visor).
  - Miniatura portátil del cubo ("mini-cubo" de apuntes personales para estudiantes entre sesiones) queda fuera del alcance actual.

## 3. Roles y Modos de Interacción
### A. Rol Profesor (Anfitrión / Host)
- **Ubicación Inicial:** Interior del cubo transparente.
- **Capacidades:**
  - Desplazamiento libre dentro del cubo hacia cualquiera de las 4 caras.
  - Escritura y dibujo a mano alzada en tiempo real con marcadores virtuales de colores y borrador físico sobre las 3 caras principales.
  - Instanciación, agarre, traslado y anclaje de objetos 3D temáticos (cubos, esferas, cilindros, globos terráqueos, etc.) sobre las superficies.
  - Control total desde la cara auxiliar: limpieza instantánea por cara, exportación a PNG de cada cara y grabación de video desde la perspectiva (POV) de sus gafas VR.
  - Gestión de turnos: aceptar o denegar solicitudes de palabra de los estudiantes.

### B. Rol Estudiante (Cliente / Client)
- **Ubicación Inicial:** Espacio exterior (área delimitada por una cúpula transparente alrededor del cubo).
- **Capacidades:**
  - Desplazamiento libre alrededor del exterior del cubo para observar la presentación en tiempo real.
  - Sin permisos iniciales de edición ni grabación.
  - **Solicitud de Intervención:** Puede levantar la mano (gesto/señal) para pedir la palabra; al ser aceptado por el profesor, ingresa temporalmente al interior del cubo y habilita las herramientas de edición para participar.

## 4. Stack Tecnológico
- **Motor Gráfico:** Unity (Universal Render Pipeline - URP + Shader Graph).
- **Framework XR:** OpenXR + **XR Interaction Toolkit** (selección por rayo, agarre directo y locomoción).
- **Arquitectura Multijugador:** **Netcode for GameObjects** (`com.unity.netcode.gameobjects` v2.13.3) con **Unity Transport** (topología Host/Client).
- **Renderizado de Pizarra:** Cámaras ortogonales dedicadas (`Draw Camera`) + `RenderTexture` (`2048x1024`, `R8G8B8A8_UNORM`) + Shader Graph con recorte de transparencia por luminancia (RGB a HSV + Step).
- **Audio:** Unity `Microphone` API para captura de voz asociada a la sesión/superficie.
