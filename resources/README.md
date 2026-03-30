# Resources — Sistema de Archivos AoPak

Los recursos del juego (gráficos, mapas, sonidos, datos) se distribuyen empaquetados en archivos `.aopak`. Este directorio contiene las herramientas para crear y verificar esos archivos.

## Estructura

```
resources/
  compressor/
    lib/          # Librería AoPak: lector, escritor, formato, manifest
    app/          # Aplicación gráfica de empaquetado (Godot)
  data/           # Recursos fuente (sueltos, para desarrollo)
  output/         # Archivos .aopak generados
```

## Uso básico

La CLI se compila con `dotnet build resources/compressor/lib/CLI/AoPakCli.csproj`.

```bash
# Empaquetar una carpeta
aopak pack resources/data/INIT output/init.aopak

# Verificar integridad
aopak verify output/init.aopak

# Listar contenido
aopak list output/init.aopak

# Desempaquetar
aopak unpack output/init.aopak /tmp/init-unpacked
```

## Cómo funciona la clave de cifrado

Cada `.aopak` está cifrado con una clave de 32 bytes (AMK) derivada de una passphrase via SHA256.

El cliente del juego tiene la AMK pre-computada y ofuscada en `client/Scripts/Data/Resources/AopakKeyStore.cs`. La CLI la deriva en tiempo de ejecución.

**Por defecto el repositorio funciona sin configuración.** La CLI y el cliente usan la misma clave por defecto. No hay que tocar nada para desarrollar.

## Cambiar la clave de cifrado

Si querés usar una clave propia (por ejemplo para una distribución privada), son tres pasos:

**Paso 1 — Generar los arrays para el cliente:**

```bash
aopak keygen --key "tu-passphrase-secreta"
```

Esto imprime los 6 arrays de bytes. Pegálos en `client/Scripts/Data/Resources/AopakKeyStore.cs` reemplazando los valores existentes y recompilá el cliente.

**Paso 2 — Configurar la CLI:**

Seteá la variable de entorno antes de empaquetar:

```bash
export AOPAK_KEY="tu-passphrase-secreta"
aopak pack resources/data/INIT output/init.aopak
```

O usá el flag `--key` directamente en cada comando.

**Paso 3 — Reempaquetar todos los archivos:**

Todos los `.aopak` existentes hay que regenerarlos con la nueva clave. Los archivos viejos no van a ser legibles por el cliente nuevo.

> **Nota:** la passphrase nunca se guarda en el repositorio. Solo se guardan los arrays derivados en `AopakKeyStore.cs`. Podés ver `.env.example` como referencia del nombre de la variable de entorno.
