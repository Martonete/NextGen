# Continuidad de trabajo - 2026-08-12

Este documento es el punto de reanudacion para el trabajo de Martin en Argentum Nextgen.
Leer tambien `AGENTS.md` antes de modificar codigo. El proyecto local y su historial Git son la
fuente de verdad; no eliminar ni reemplazar assets masivamente sin confirmar.

## Estado publicado en esta tanda

- Se porto el render de sombras AO20 al cliente (`Ao20ShadowRenderer`), con silueta compuesta para
  personajes/NPC, sombras para arboles y la lista historica de L3. Respeta las opciones de sombras.
- Se incorporo el render de mundo/vision inspirado en Eternal: viewport de juego, niebla/rango de
  vision, consola transparente y ciclo dia/tarde/noche. Dia y tarde fueron ajustados a pedido de
  Martin (dia menos claro, tarde algo mas naranja, noche conservada).
- La interfaz principal usa el arte de HUD nuevo en `resources/data/UI/main_hud.png`; se corrigieron
  fullscreen y escalado 1080p sin volver al HUD anterior. El viewport mantiene la resolucion interna
  y el HUD se adapta por anclas, no por reescalado libre del mapa.
- Se incorporaron los graficos fuente de la rama/repo original al arbol `resources/data/Graficos` y
  al indice. Los PNG importados para mapear conservan la convencion `100000 + numeroOriginal`.
  Ejemplo: la lamina original 30326 se identifica como archivo 130326 en el indice activo.
- El World Editor tiene galeria de laminas importadas, busqueda, paginacion, vista previa, seleccion
  de region y uso de lamina completa; los pickers relevantes son `GraphicsSheetPickerPopup.cs` y
  `RawGrhPickerPopup.cs`.
- El editor ahora muestra un selector explicito `Pintar en: L1 Terreno / L2 Piso / L3 Objetos /
  L4 Techos`. La visibilidad de capas esta separada en el menu `Ver`.
- Se igualo el anclaje de GRH del World Editor con `CharRenderer.DrawGrh` del cliente (ancla,
  redondeo y region UV con wrapping), para que los GRH grandes no se vean corridos entre editor e
  juego.
- Las particulas colocadas en el mapa son persistentes y no vencen por `LifeCounter`; ademas toda
  particula recibe una vida minima de un tick. Las particulas transitorias de personajes conservan
  su timeout.
- El mapa 197 y sus archivos de cliente/servidor estan incluidos en el arbol de trabajo.

## Convenciones de capas para mapear

- **L1 Terreno:** suelo base y tiles repetibles.
- **L2 Piso:** caminos, escaleras, alfombras y detalles por debajo del personaje. Un personaje se
  dibuja encima de esta capa.
- **L3 Objetos:** muros, edificios, arboles y objetos que deben tapar al personaje cuando la
  profundidad lo corresponde.
- **L4 Techos:** techos/overlay superior.

El motor AO es una grilla plana: una escalera no agrega altura fisica. Para un altar/edificio,
colocar las escaleras transitables en L2, la fachada/muros en L3 y marcar como bloqueadas solo las
baldosas de estructura. Asi el jugador no atraviesa las paredes y se ve sobre los escalones.

## Validacion hecha

```powershell
cd C:\Users\marti\Documents\AORust\argentum-nextgen\client
dotnet build

cd C:\Users\marti\Documents\AORust\argentum-nextgen\tools\world-editor
dotnet build
```

Ambas compilaciones finalizaron sin errores ni advertencias el 2026-08-12.

## Arranque operativo

Servidor:

```powershell
cd C:\Users\marti\Documents\AORust\argentum-nextgen
docker compose up -d --build ao-server
```

Cliente:

```powershell
powershell -ExecutionPolicy Bypass -File 'C:\Users\marti\Documents\AORust\argentum-nextgen\Open-LocalClient.ps1'
```

World Editor:

```powershell
powershell -ExecutionPolicy Bypass -File 'C:\Users\marti\Documents\AORust\argentum-nextgen\Open-WorldEditor.ps1'
```

## Siguiente paso recomendado

Probar en editor y cliente una misma estructura grande (altar/portal) despues del build actual;
confirmar con captura que su pie coincide. Ajustar despues los tiles bloqueados y la distribucion
L2/L3 del altar. Si vuelve a haber diferencia visual, identificar el GRH, la coordenada de tile y
la capa antes de cambiar offsets globales.
