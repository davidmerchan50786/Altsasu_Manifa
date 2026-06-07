#!/bin/bash
# run_blender_archetypes.sh — Genera 12 arquetipos vascos LOD0-3 en FBX
# Proyecto: Altsasu Manifa Unity HDRP — B5
#
# Uso:
#   cd /home/user/Altsasu_Manifa
#   bash Tools/run_blender_archetypes.sh
#
# Requisitos:
#   - Blender 3.x o 4.x instalado y en PATH, o ajustar BLENDER_BIN abajo
#   - Sin necesidad de GUI (--background)
#
# Output: Assets/Models/Archetypes_Maya/*.fbx  (12 archivos, uno por arquetipo)

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PROJECT_ROOT="$(dirname "$SCRIPT_DIR")"
PYTHON_SCRIPT="$SCRIPT_DIR/blender_archetypes.py"

# Ajustar si Blender no está en PATH
BLENDER_BIN="${BLENDER_PATH:-blender}"

echo "[B5] Altsasu Manifa — Generador de arquetipos vascos"
echo "[B5] Script: $PYTHON_SCRIPT"
echo "[B5] Output: $PROJECT_ROOT/Assets/Models/Archetypes_Maya/"
echo ""

if ! command -v "$BLENDER_BIN" &>/dev/null; then
    echo "[B5] ERROR: Blender no encontrado en PATH."
    echo "       Instala Blender o define la variable BLENDER_PATH:"
    echo "         export BLENDER_PATH=/ruta/a/blender"
    echo "         bash Tools/run_blender_archetypes.sh"
    echo ""
    echo "       En Ubuntu/Debian:  sudo apt install blender"
    echo "       Snap:              sudo snap install blender --classic"
    echo "       Flatpak:           flatpak install flathub org.blender.Blender"
    echo "       Descarga manual:   https://www.blender.org/download/"
    exit 1
fi

BLENDER_VERSION=$("$BLENDER_BIN" --version 2>/dev/null | head -1)
echo "[B5] Blender: $BLENDER_VERSION"
echo ""

"$BLENDER_BIN" --background --python "$PYTHON_SCRIPT"

EXIT_CODE=$?
if [ $EXIT_CODE -eq 0 ]; then
    echo ""
    echo "[B5] COMPLETADO. FBX generados en:"
    ls -lh "$PROJECT_ROOT/Assets/Models/Archetypes_Maya/"*.fbx 2>/dev/null || echo "  (directorio vacío — revisar errores arriba)"
else
    echo "[B5] ERROR: Blender terminó con código $EXIT_CODE"
    exit $EXIT_CODE
fi
