# Inmovilizar: lazo verde oscuro

FX 8 activa una atadura de tres vueltas alrededor del torso: se cierra en
0,3 segundos, forma un nudo frontal y desaparece al completar 1,1 segundos.
Es una animación del lanzamiento, no un indicador de la duración real del estado.
No cambia las reglas ni duración de inmovilización.

Inmovilizar (24), Paralizar (9) y Paralizar NPCs (39) comparten FX 8 en los
datos. CreateFX ahora reserva loops=-24 para Inmovilizar: el cliente muestra
su envolvente verde; FX 8 con loops normales muestra Paralizar, dos grilletes
azul acero con cierre rígido frontal. El tamaño del paquete no cambia;
clientes anteriores convierten loops negativos en una reproducción clásica.
No hay destellos: se omite el haz de viaje para FX 8 y el cliente descarta
su impacto genérico 106. La envolvente usa color mate sin reflejo brillante.
Con Mundo reactivo o partículas desactivados se conserva el FX clásico.

WorldRenderer.Binding.cs se dibuja inmediatamente antes/después del cuerpo,
con mezcla normal para conservar el verde oscuro. Respeta profundidad de
personajes, árboles y techos, invisibilidad, muerte, pausa y cambio de mapa.
Un temporizador por personaje y puntos reutilizados evitan crear emisores.
Repetir el lanzamiento reinicia el lazo; ClearFX lo cancela.

RenderSmoke verifica paquetes, repetición, expiración y cancelación, y genera
binding-closing.png y binding-tight.png para revisión visual.
