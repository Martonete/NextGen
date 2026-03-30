# RuthnarIndexer — Análisis Completo del Codebase

> Generado el 2026-03-08

---

## 1. Descripción General

**RuthnarIndexer** es una herramienta de escritorio construida en **Godot 4.3 (GDScript)** para indexar sprites del juego **Argentum Online**. Su función principal es leer y escribir el archivo binario `Graficos.ind`, que mapea frames de spritesheets a entradas GRH (Grafic Resource Handle).

---

## 2. Estructura de Archivos

```
RuthnarIndexer/
├── project.godot                  # Configuración del proyecto Godot 4.3
├── Main.tscn                      # Escena principal
├── scripts/
│   ├── Main.gd                    # Controlador principal + UI (2288 líneas)
│   ├── SpriteCanvas.gd            # Canvas interactivo (576 líneas)
│   ├── FrameDetector.gd           # Algoritmos de detección (167 líneas)
│   ├── FramePreviewPanel.gd       # Preview + animación (85 líneas)
│   └── GrhIO.gd                   # I/O binario del formato .ind (197 líneas)
├── Controls/                      # Vacío (reservado)
├── Export/                        # Vacío (reservado)
└── Models/                        # Vacío (reservado)
```

**Total: ~3,313 líneas de GDScript en 5 scripts.**

---

## 3. Arquitectura y Flujo de Datos

```
Cargar .ind / carpeta cliente
         │
         ▼
  GrhIO.load_ind()          ← Parser binario del formato Graficos.ind
         │
         ▼
   _grh_data (dict)         ← Base de datos en memoria de todas las entradas GRH
         │
  Seleccionar imagen
         │
         ▼
  _load_image_from_os_path()
   ├── Intento 1: carga directa
   ├── Intento 2: strip de metadata PNG (iCCP, sRGB, gAMA, etc.)
   └── Intento 3: escritura a archivo temporal y recarga
         │
         ▼
  FrameDetector.detect_blobs_indexed()  ← Pre-computa mapa de blobs O(1)
         │
         ▼
    SpriteCanvas               ← Display + interacción
   ├── Hover sobre blobs (O(1) lookup)
   ├── Dibujo manual de frames
   ├── Resize / Move (8 handles)
   └── Zoom / Pan
         │
  ┌──────┴──────┐
  │             │
Detección    Manual
Grid/Blob    Draw
  │             │
  └──────┬──────┘
         ▼
  _current_frames[]          ← Frames activos de la imagen seleccionada
         │
         ▼
  ✅ Botón INDEXAR
  _on_index_image()
         │
         ▼
  GrhIO.save_ind()           ← Escritura binaria del .ind actualizado
```

---

## 4. Estructuras de Datos Clave

### `_grh_data` — Base de datos GRH en memoria
```gdscript
_grh_data = {
  "version": 12,
  "max_index": 5000,
  "entries": {
    1001: {
      "grh_index": 1001,
      "num_frames": 1,       # Frame estático
      "file_num": 42,        # Número de archivo PNG (gfx_042.png)
      "sx": 10, "sy": 20,    # Posición en el spritesheet
      "width": 32, "height": 32
    },
    2000: {
      "grh_index": 2000,
      "num_frames": 4,       # Frame animado
      "frame_indices": [1001, 1002, 1003, 1004],
      "speed": 8.0
    }
  }
}
```

### `_current_frames` — Frames de la imagen activa
```gdscript
_current_frames = [
  {"sx": 0,  "sy": 0, "w": 32, "h": 32, "grh_index": 5001, "file_num": 99},
  {"sx": 32, "sy": 0, "w": 32, "h": 32, "grh_index": 5002, "file_num": 99},
]
```

---

## 5. Descripción de Cada Script

### `GrhIO.gd` — I/O binario
- `load_ind(path)` — Carga `Graficos.ind` con auto-detección de versiones de header
- `save_ind(path, data)` — Guarda el índice en formato binario
- `to_text(data)` / `from_text(text)` — Conversión a/desde representación de texto
- Implementa conversión de signed int32/int16 para compatibilidad multiplataforma
- Tres intentos de detección de header por variantes históricas del formato

### `FrameDetector.gd` — Detección de frames
- `detect_blobs_indexed()` — BFS con indexado entero de píxeles, filtro por umbral alpha (3% default), detección de color-key negro. Resultado: `PackedInt32Array` para lookup O(1)
- `detect_grid()` — Detección por grilla regular con cell size + offset + margin configurable

### `SpriteCanvas.gd` — Canvas interactivo
- Dibujado manual por click-drag
- Selección y transformación de frames (8 handles: 4 esquinas + 4 bordes)
- Modos snap: Ninguno / Múltiplo / Potencia-de-2 / Cuadrado Potencia-de-2
- Zoom con rueda (pasos de 1.15x) + Pan con click medio/derecho
- Hover sobre blobs con resaltado amarillo
- Señales: `frame_drawn`, `frame_selected`, `blob_clicked`, `frame_resized`, `frame_delete_pressed`

### `FramePreviewPanel.gd` — Preview
- Muestra frames individuales o animaciones multi-frame
- Fondo damero (visualización de transparencia)
- Escalado fit-to-panel manteniendo aspect ratio
- Overlay con contador de frames y dimensiones en píxeles

### `Main.gd` — Controlador principal (monolítico)
- Gestión completa del estado de la aplicación
- 9 ventanas flotantes: Preview, Animación, Lista de frames, Propiedades, Split, Detección automática, Snap, Creador de GRH animado, Editor INIT
- Carga de imágenes con 3 niveles de fallback para PNGs problemáticos
- Ordenamiento numérico de archivos (`gfx_042` antes de `gfx_302`)
- Thumbnails con lazy loading (5 por frame) para no bloquear la UI
- Preferencias persistentes en `user://indexer_prefs.cfg` (hasta 8 clientes recientes)
- Soporte para `Personajes.ind` (6 parámetros por cuerpo) y `Fxs.ind`

---

## 6. Flujos de Trabajo Soportados

| Flujo | Descripción |
|---|---|
| Cargar .ind directo | Abre un `Graficos.ind` standalone |
| Cargar carpeta de imágenes | Lista y procesa PNGs de una carpeta |
| Cargar cliente completo | Auto-detecta subcarpetas `Graficos/` e `INIT/` |
| Detección automática (blobs) | BFS sobre píxeles con umbral alpha |
| Detección automática (grid) | Grilla con cell size + offsets |
| Dibujo manual | Click-drag para crear frames a mano |
| Split de frames | Divide un frame en subgrilla NxM |
| Creación de GRH animado | Compone animación desde frames estáticos |
| Edición de archivos INIT | Editor de texto para .ini/.ao/.txt |
| Copiar PNG al cliente | Copia con naming por FileNum |

---

## 7. Lo que Funciona Bien

- **Separación de responsabilidades:** GrhIO / FrameDetector / SpriteCanvas / Main bien delimitados
- **Hover O(1):** Mapa de blobs pre-computado evita BFS por frame en cada movimiento del mouse
- **Robustez PNG:** 3 niveles de fallback + strip de metadata problemática
- **Lazy loading de thumbnails:** 5 por tick para no congelar la UI
- **UX cuidado:** Frames con colores cíclicos, dimensiones en vivo, checkerboard, shortcuts teclado
- **Formatos múltiples:** Graficos, Personajes y Fxs en un solo tool

---

## 8. Problemas y Áreas de Mejora

### Crítico
| Problema | Descripción |
|---|---|
| **Sin undo/redo** | No hay historial de acciones. Un error borra trabajo |
| **GrhIndex duplicado silencioso** | Si se indexa con un índice ya existente, sobreescribe sin avisar |
| **Referencias animadas no validadas** | Un GRH animado puede referenciar índices inexistentes |

### Arquitectura
| Problema | Descripción |
|---|---|
| **Main.gd monolítico** | 2,288 líneas mezclan lógica de negocio con construcción de UI |
| **9 ventanas flotantes manuales** | Posicionamiento hardcodeado, sin persistencia de posición |
| **Sin caché de imágenes** | Cada vez que se selecciona una imagen se recarga desde disco |

### UX / Performance
| Problema | Descripción |
|---|---|
| **Sin barra de progreso** | Detección de blobs en imágenes grandes bloquea sin feedback |
| **Límites silenciosos** | El GRH viewer trunca a 300 frames sin avisar |
| **Sin operaciones batch** | No se pueden procesar múltiples imágenes en secuencia automática |
| **Sin persistencia de posición de ventanas** | Las ventanas floantes siempre arrancan en la misma posición |

### Código
| Problema | Descripción |
|---|---|
| **Números mágicos** | `263` (header PNG), `1.15` (zoom step), `0.58` (ancho útil) sin constantes nombradas |
| **Notación húngara** | `_win_*`, `_m_*`, `_spin_*`, `_lbl_*` — inconsistente con estilo moderno GDScript |
| **Comentarios en español** | Dificulta colaboración internacional |
| **Sin validación de CRC** en chunks PNG | El strip de metadata no verifica integridad |

---

## 9. Deuda Técnica Estimada

```
Alta prioridad:
  [ ] Undo/Redo (CommandPattern o ActionStack)
  [ ] Detección y aviso de GrhIndex duplicado
  [ ] Validar referencias de GRH animados al guardar

Media prioridad:
  [ ] Extraer UIBuilder de Main.gd
  [ ] Crear clase GrhDatabase (wrapper sobre _grh_data)
  [ ] Barra de progreso para detección y carga
  [ ] Caché de imágenes en memoria

Baja prioridad:
  [ ] Persistir posición de ventanas flotantes
  [ ] Operaciones batch sobre múltiples imágenes
  [ ] Nombrar constantes mágicas
  [ ] Docstrings en métodos públicos
```

---

## 10. Patrones de Extensión

- **Nuevo algoritmo de detección:** Agregar método a `FrameDetector.gd` y conectar desde `Main.gd`
- **Nuevo formato .ind:** Extender `GrhIO.gd` con nuevo parser (ya hay precedente con Personajes/Fxs)
- **Nuevo modo snap:** Agregar caso al enum en `SpriteCanvas.gd`
- **Nueva ventana flotante:** Seguir el patrón `_build_*_window()` de Main.gd

---

*Análisis generado por Claude Code — claude-sonnet-4-6*
