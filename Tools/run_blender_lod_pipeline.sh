#!/bin/bash
# B4 — Genera mallas LOD0-3 desde buildings_fusion_final.json
# Requiere: Blender 3.6+ en PATH
# Uso: bash Tools/run_blender_lod_pipeline.sh [--max_buildings 100] [--archetype pre1940_piedra]

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_DIR="$(dirname "$SCRIPT_DIR")"

mkdir -p "$PROJECT_DIR/Assets/Models/Buildings_Generated"

# Detectar Blender
BLENDER=$(command -v blender 2>/dev/null \
    || command -v blender-3.6 2>/dev/null \
    || ls /Applications/Blender.app/Contents/MacOS/Blender 2>/dev/null \
    || ls "C:/Program Files/Blender Foundation/Blender 3.6/blender.exe" 2>/dev/null \
    || echo "")

if [ -z "$BLENDER" ]; then
    echo "ERROR: Blender no encontrado en PATH."
    echo "Instala Blender 3.6+ y asegúrate de que esté en PATH."
    echo "Windows: agrega C:\\Program Files\\Blender Foundation\\Blender 3.6\\ al PATH"
    exit 1
fi

echo "Usando Blender: $BLENDER"
echo "Proyecto: $PROJECT_DIR"
echo "Generando LODs para edificios de Alsasua..."

"$BLENDER" --background --python "$SCRIPT_DIR/blender_lod_pipeline.py" -- \
    --input  "$PROJECT_DIR/Assets/AlsasuaData/buildings_fusion_final.json" \
    --output "$PROJECT_DIR/Assets/Models/Buildings_Generated/" \
    --textures "$PROJECT_DIR/Assets/AlsasuaData/FacadeTextures/Processed/" \
    "$@"

echo "Pipeline completado. FBX en Assets/Models/Buildings_Generated/"
echo "Log: Assets/AlsasuaData/blender_pipeline_log.json"
