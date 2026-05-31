#!/bin/bash
# gimp_enhance_facades.sh
# Mejora batch de fachadas con GIMP script-fu
# Uso: bash Tools/gimp_enhance_facades.sh
#
# Aplica por cada imagen PNG en Processed/:
#   - Stretch de niveles automático (normalización global)
#   - Reducción de saturación -15 (para arquitectura vasca terrosa)
#   - Unsharp Mask (radio 1.5, cantidad 0.4, umbral 0) — detalle de texturas
#
# Requisito: GIMP instalado y en PATH (gimp ó flatpak run org.gimp.GIMP)
# Si GIMP no está disponible, el script continúa sin crashear.

set -euo pipefail

# ---------------------------------------------------------------------------
# Configuración de rutas
# ---------------------------------------------------------------------------
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(dirname "$SCRIPT_DIR")"
PROCESSED_DIR="$PROJECT_ROOT/Assets/AlsasuaData/FacadeTextures/Processed"

# ---------------------------------------------------------------------------
# Verificaciones previas
# ---------------------------------------------------------------------------
if [ ! -d "$PROCESSED_DIR" ]; then
    echo "ERROR: Directorio $PROCESSED_DIR no encontrado."
    echo "Ejecuta primero: python3 Tools/process_streetview_photos.py"
    exit 1
fi

# Detectar GIMP (instalación nativa o flatpak)
GIMP_CMD=""
if command -v gimp &>/dev/null; then
    GIMP_CMD="gimp"
elif flatpak list 2>/dev/null | grep -q "org.gimp.GIMP"; then
    GIMP_CMD="flatpak run org.gimp.GIMP"
fi

if [ -z "$GIMP_CMD" ]; then
    echo "GIMP no encontrado en el sistema."
    echo "Instala GIMP con: sudo apt install gimp   ó   flatpak install flathub org.gimp.GIMP"
    echo "Saltando enhancement — las texturas procesadas por Python ya son válidas."
    exit 0
fi

# ---------------------------------------------------------------------------
# Contar imágenes
# ---------------------------------------------------------------------------
shopt -s nullglob
IMAGES=("$PROCESSED_DIR"/*_facade.png)
shopt -u nullglob

if [ ${#IMAGES[@]} -eq 0 ]; then
    echo "No se encontraron imágenes *_facade.png en $PROCESSED_DIR"
    echo "Ejecuta primero: python3 Tools/process_streetview_photos.py"
    exit 0
fi

echo "GIMP batch enhancement — ${#IMAGES[@]} imágenes en $PROCESSED_DIR"
echo "Usando: $GIMP_CMD"
echo ""

ENHANCED=0
FAILED=0

# ---------------------------------------------------------------------------
# Procesar cada imagen
# ---------------------------------------------------------------------------
for img in "${IMAGES[@]}"; do
    [ -f "$img" ] || continue

    echo -n "  Procesando: $(basename "$img") ... "

    # Script-fu GIMP en una línea:
    #   1. Cargar imagen
    #   2. Obtener drawable activo
    #   3. gimp-levels-stretch  — normalización automática de niveles
    #   4. gimp-drawable-hue-saturation  — reducir saturación -15
    #   5. plug-in-unsharp-mask  — enfocar detalles de textura
    #   6. Guardar como PNG (sobreescribe)
    #   7. Eliminar de memoria
    SCRIPT="(let* (
        (image    (car (gimp-file-load RUN-NONINTERACTIVE \"$img\" \"$img\")))
        (drawable (car (gimp-image-get-active-drawable image))))
      (gimp-levels-stretch drawable)
      (gimp-drawable-hue-saturation drawable HUE-RANGE-ALL 0 0 -15 0)
      (plug-in-unsharp-mask RUN-NONINTERACTIVE image drawable 1.5 0.4 0)
      (file-png-save RUN-NONINTERACTIVE image drawable \"$img\" \"$img\" 0 9 1 1 1 1 1)
      (gimp-image-delete image))"

    if $GIMP_CMD -i -b "$SCRIPT" -b "(gimp-quit 0)" 2>/dev/null; then
        echo "OK"
        ENHANCED=$((ENHANCED + 1))
    else
        echo "FALLO (se mantiene versión sin enhancement)"
        FAILED=$((FAILED + 1))
    fi
done

# ---------------------------------------------------------------------------
# Resumen
# ---------------------------------------------------------------------------
echo ""
echo "Enhancement completado."
echo "  Procesadas con GIMP: $ENHANCED"
if [ $FAILED -gt 0 ]; then
    echo "  Fallidas (se mantienen originales): $FAILED"
fi
