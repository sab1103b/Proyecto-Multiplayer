# 03. FLUJOS DE INTERACCIÓN, DECISIONES TÉCNICAS Y ROADMAP

## 1. Decisiones de Arquitectura (Lecciones Aprendidas)
- **Descarte de Raycast para Dibujo:** La primera iteración usó `Physics.Raycast` + coordenadas UV (`textureCoord`) para pintar, lo cual generó fallos de precisión y pérdida de contacto continuo entre el marcador y el muro. **Estándar obligatorio:** Mantener el sistema de cámara ortogonal por cara + `RenderTexture` + Shader Graph.
- **Separación de Superficie de Control:** No saturar las 3 caras de exposición con botones flotantes; toda la UI administrativa (Borrar, Exportar, Figuras) reside en la 4ª cara auxiliar del cubo.

## 2. Flujos Críticos a Completar / Refinar en Unity
1. **Sincronización Multijugador de Trazos y Objetos (Netcode):**
   - Garantizar que cuando el Host (Profesor) o un Cliente autorizado (Estudiante) dibuje en una cara o instancie una figura 3D, el estado se replique correctamente a todos los clientes conectados.
2. **Sistema de Permisos Dinámicos (Levantar la Mano):**
   - Implementar la máquina de estados del estudiante: `EstadoObservador` (fuera del cubo, sin herramientas) -> `SolicitudPendiente` (señal visual/UI hacia el profesor) -> `EstadoIntervencion` (teletransporte dentro del cubo + activación temporal de marcadores/agarre) -> retorno al exterior al finalizar.
3. **Grabación POV y Asociación de Audio:**
   - Vincular la captura de micrófono (`Microphone`) y la grabación de vista del visor del profesor a la superficie activa durante la exposición.

## 3. Entregables del Proyecto
- Escena principal funcional en Unity (`OpenXR` + `XR Interaction Toolkit` + `Netcode for GameObjects`).
- Prefabs configurados: `VRNetworkPlayer`, `CuboPresentacion` (con 3 `Draw Camera` + 3 `RenderTexture` + `CanvasManager` por cara), `Marcadores` (Rojo, Verde, Azul), `Borrador` (Negro) y `Figuras3D`.
- Build ejecutable y video demostración del flujo completo Profesor–Estudiante.
