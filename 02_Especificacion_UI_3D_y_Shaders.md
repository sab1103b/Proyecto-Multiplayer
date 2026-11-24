# 02. ESPECIFICACIÓN DE INTERFAZ 3D (SPATIAL UI) Y SISTEMA DE SHADERS

## 1. Arquitectura Espacial y Estética Visual
- **Entorno Exterior:** Plataforma circular lobulada delimitada por una cúpula semitransparente azulada sobre entorno natural low-poly (árboles, terreno verde).
- **Estructura Central (El Cubo):** 4 paredes de vidrio/material semitransparente cian tenue (`Alpha` bajo) que permiten visibilidad bidireccional entre el interior (profesor) y exterior (estudiantes).
- **Avatares en Red:** Representación minimalista de cabeza y manos flotantes en color azul cian (`#38BDF8` aprox.) con rayos láser de interacción (`XR Ray Interactor`).

## 2. Paneles UI Diegéticos (World Space Canvas — Cara Auxiliar)
Los paneles están organizados en cuadrícula flotante dentro de la superficie auxiliar de control con tarjetas oscuras translúcidas (`#1E242B` con opacidad) y bordes redondeados:

1. **Panel "CUBO VR — Selección de Rol y Sala":**
   - Título con isotipo de cubo multicolor + subtítulo *"Elige tu rol para comenzar"*.
   - **Botón Profesor (Ámbar/Naranja `#E69D35`):** *"Profesor — Crear sala"* (inicia Host y teletransporta al interior del cubo).
   - **Botón Estudiante (Turquesa/Verde `#2A9D8F`):** *"Estudiante — Unirse a la sala"* (conecta como Cliente en el exterior).
   - **Panel Multijugador Alternativo (`Start Multiplayer`):** Botones azules sólidos `Join` y `Create` para conexión directa de red.

2. **Panel "Borrar" (Limpieza de Superficies):**
   - Encabezado con ícono de papelera roja.
   - Botones rojos (`#C94A4A`): `[Cara 1]`, `[Cara 2]`, `[Cara 3]` — Ejecutan limpieza total instantánea del `RenderTexture` asociado.

3. **Panel "Exportar" (Captura de Lienzos):**
   - Encabezado con ícono de descarga verde azulado.
   - Botones verde agua (`#3B8E8C`): `[Cara 1]`, `[Cara 2]`, `[Cara 3]` — Exportan el lienzo actual a PNG en almacenamiento persistente.

4. **Panel "Figuras" (Generador de Objetos 3D):**
   - Encabezado con ícono de primitivas geométricas.
   - Cuadrícula 2x2 de botones visuales interactivos: **Cubo**, **Esfera**, **Cilindro** y **Globo Terráqueo** (instancia prefabs con `XRGrabInteractable` y `NetworkObject`).

## 3. Pipeline Técnico de Escritura y Borrado (RenderTexture + Shader Graph)
- **Captura de Trazos (Evitando Raycast):** Cada una de las 3 caras tiene una cámara ortogonal (`Draw Camera`, Layer: `Marker`, `Target Eye: Both`, `Filter Mode: Point`) apuntando al plano de escritura. Al entrar un marcador en el rango de profundidad de la cámara, sus trazos se renderizan cuadro a cuadro en una `RenderTexture` de `2048 x 1024 px` (`R8G8B8A8_UNORM`, sin Mipmaps, `Clamp`).
- **Shader Graph de Transparencia y Borrador:**
  - Lee el `Sample Texture 2D` de la `RenderTexture`.
  - Convierte el color de `RGB` a `HSV` (`Colorspace Conversion`) y evalúa el canal de luminosidad/valor (`V`) mediante un nodo `Step`.
  - **Regla del Borrador:** El objeto borrador pinta en **negro puro** (`V ≈ 0`). El shader detecta el negro puro (con tolerancia para bordes suavizados) y asigna `Alpha = 0` en el fragment shader, revelando el material transparente del cubo en lugar de pintar encima.
