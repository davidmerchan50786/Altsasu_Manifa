# Flujo de generación AAA+ Altsasua

## 1. Descargar datos oficiales (opcional pero recomendado)

Abre una terminal en la carpeta `Tools` del proyecto:

```bash
cd E:\Desk\DAM\Altsasu_Manifa\Tools
pip install requests shapely pyproj
python descargar_ign_navarra.py
```

Tarda ~2-5 minutos. Descarga:
- Edificios con altura y plantas reales (Catastro/OSM)
- Todas las carreteras y calles
- Vías del tren + estaciones
- Hidrografía (río Arakil, arroyos)
- Bosques y uso del suelo

Los JSON se guardan en `Assets/AlsasuaData/IGN/`.

## 2. Importar en Unity

En Unity:

`Altsasu GTA → Territorio Real → ★ Importar Datos IGN Navarra`

Esto copia los datos descargados al lugar correcto (haciendo backup de los antiguos).

## 3. Generar la escena AAA+ paso a paso

En orden:

1. `Altsasu GTA → Territorio Real → ★ Crear Terrain + Ortofoto (Editor)`
   - Crea el terreno desde el DEM LiDAR
   - Aplica la ortofoto PNOA real

2. `Altsasu GTA → Territorio Real → ★ Generar Edificios OSM Reales`
   - Extruye TODOS los edificios OSM con su footprint y altura real
   - Añade tejados a 2 aguas con teja roja al casco viejo
   - Añade ventanas procedurales a las fachadas
   - Chimeneas ocasionales

3. `Altsasu GTA → Territorio Real → ★ Generar Infraestructura Completa`
   - TODAS las carreteras con ancho según tipo OSM
   - Autovía N-1, principales, secundarias, residenciales, footways
   - Aceras a los lados de carreteras grandes
   - Línea blanca central en autovías y primarias
   - Vías del tren con balasto + traviesas
   - Río Arakil
   - Estación de tren

4. `Altsasu GTA → Territorio Real → ★ Mobiliario Urbano (farolas, árboles)`
   - Farolas a ambos lados de carreteras principales (cada 25-40m)
   - Árboles en calles residenciales (cada 18m)
   - Bancos alrededor de Herriko Plaza

5. `Altsasu GTA → Utilidades → ★ Quitar brillo del terreno (mate)`
   - Pone el terreno mate, sin reflejos tipo agua

6. `Altsasu GTA → ★ FIX TODO Y AAA+ EN UN CLIC ★`
   - Coloca jugador, cámara, iluminación, post-proceso final

7. **▶ Play** → estás en Altsasua con calidad AAA+

## Controles

| Tecla | Acción |
|---|---|
| WASD | Mover |
| Shift | Correr |
| Espacio | Saltar |
| Ratón (botón derecho) | Girar cámara |
| Escape | Pausa |
| M | Manifestación |
| 1-9 | Cambiar arma |
| G | Grafiti |
