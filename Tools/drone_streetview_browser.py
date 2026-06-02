#!/usr/bin/env python3
"""
DRON VIRTUAL STREET VIEW (BROWSER) — Altsasu Manifa — GRATIS
=============================================================
Automatiza un navegador para recorrer Google Street View punto por punto
y capturar pantallazos de cada fachada. Sin API key, sin pagar nada.

Requiere: pip install selenium
Navegador: Chrome + chromedriver (o Firefox + geckodriver)

El script:
  1. Lee drone_waypoints.json (generado por drone_streetview_capture.py --plan-only)
  2. Abre Chrome en modo headless (o visible si --visible)
  3. Para cada waypoint: navega a Street View → espera carga → screenshot
  4. Recorta la zona de fachada → guarda como textura
  5. Salta al siguiente punto cada 2-3 segundos

SETUP (Windows):
  pip install selenium
  Descarga chromedriver: https://googlechromelabs.github.io/chrome-for-testing/
  Pon chromedriver.exe en E:\chromedriver\ o en PATH

USO:
  # Primero generar plan de vuelo (sin descargar):
  python Tools/drone_streetview_capture.py --plan-only

  # Luego ejecutar el dron browser:
  python Tools/drone_streetview_browser.py
  python Tools/drone_streetview_browser.py --visible          # ver el navegador
  python Tools/drone_streetview_browser.py --limit 50         # solo 50 capturas
  python Tools/drone_streetview_browser.py --zona iglesia     # solo zona iglesia
  python Tools/drone_streetview_browser.py --step-delay 3     # 3s entre capturas

ALTERNATIVA SIN SELENIUM (Mapillary gratis):
  # Obtener token en https://www.mapillary.com/developer (es gratis)
  python Tools/drone_streetview_capture.py --mapillary-token TU_TOKEN

SALIDA:
  Assets/AlsasuaData/FacadeTextures/StreetView_Auto/{building_id}.png
"""

import argparse
import json
import math
import os
import sys
import time
from pathlib import Path

SCRIPT_DIR  = Path(__file__).resolve().parent
PROJECT_DIR = SCRIPT_DIR.parent

WAYPOINTS_JSON = PROJECT_DIR / "Assets/AlsasuaData/drone_waypoints.json"
OUTPUT_DIR     = PROJECT_DIR / "Assets/AlsasuaData/FacadeTextures/StreetView_Auto"
AAA_DIR        = PROJECT_DIR / "Assets/AlsasuaData/FacadeTextures/AAA"
REPORT_JSON    = PROJECT_DIR / "Assets/AlsasuaData/drone_browser_report.json"

# Chromedriver paths comunes en Windows
CHROMEDRIVER_PATHS = [
    Path(r"E:\chromedriver\chromedriver.exe"),
    Path(r"D:\chromedriver\chromedriver.exe"),
    Path(r"C:\chromedriver\chromedriver.exe"),
    Path(r"C:\Program Files\Google\Chrome\Application\chromedriver.exe"),
]

def find_chromedriver():
    import shutil
    cd = shutil.which("chromedriver")
    if cd: return cd
    for p in CHROMEDRIVER_PATHS:
        if p.exists(): return str(p)
    return None

def streetview_url(lat, lon, heading, pitch=0, fov=90):
    """Genera URL de Google Street View embebido con coordenadas y ángulo exactos."""
    return (
        f"https://www.google.com/maps/@{lat},{lon},3a,{fov}y,{heading}h,{90-pitch}t/data=!3m1!1e3"
    )

def run_browser_drone(waypoints, args):
    try:
        from selenium import webdriver
        from selenium.webdriver.chrome.options import Options
        from selenium.webdriver.chrome.service import Service
        from selenium.webdriver.common.by import By
        from selenium.webdriver.support.ui import WebDriverWait
        from selenium.webdriver.support import expected_conditions as EC
    except ImportError:
        print("ERROR: Selenium no instalado.")
        print("Instala con: pip install selenium")
        print()
        print("ALTERNATIVA GRATIS sin Selenium:")
        print("  1. Ve a https://www.mapillary.com/developer")
        print("  2. Crea cuenta gratuita y obtén access token")
        print("  3. Ejecuta: python Tools/drone_streetview_capture.py --mapillary-token TU_TOKEN")
        return 0

    OUTPUT_DIR.mkdir(parents=True, exist_ok=True)

    # Configurar Chrome
    options = Options()
    if not args.visible:
        options.add_argument("--headless=new")
    options.add_argument("--window-size=1920,1080")
    options.add_argument("--disable-gpu")
    options.add_argument("--no-sandbox")
    options.add_argument("--disable-dev-shm-usage")
    options.add_argument("--lang=es")
    # Deshabilitar barra de info y banners
    options.add_argument("--disable-infobars")
    options.add_experimental_option("excludeSwitches", ["enable-automation"])
    options.add_experimental_option("useAutomationExtension", False)

    cd_path = find_chromedriver()
    if cd_path:
        service = Service(executable_path=cd_path)
    else:
        service = Service()  # buscar en PATH

    print(f"Iniciando Chrome {'(visible)' if args.visible else '(headless)'}...")
    try:
        driver = webdriver.Chrome(service=service, options=options)
    except Exception as e:
        print(f"ERROR iniciando Chrome: {e}")
        print()
        print("Necesitas Chrome y chromedriver instalados.")
        print("Descarga chromedriver en: https://googlechromelabs.github.io/chrome-for-testing/")
        print("Ponlo en E:\\chromedriver\\ o en PATH")
        return 0

    driver.set_page_load_timeout(20)

    # Aceptar cookies de Google una vez
    try:
        driver.get("https://www.google.com/maps")
        time.sleep(2)
        # Buscar botón de aceptar cookies
        try:
            accept_btn = driver.find_element(By.XPATH, "//button[contains(text(),'Aceptar')]")
            accept_btn.click()
            time.sleep(1)
        except:
            pass
    except:
        pass

    captured = 0
    failed = 0
    skipped = 0

    for i, wp in enumerate(waypoints):
        bid = wp["building_id"]
        out_path = OUTPUT_DIR / f"{bid}.png"

        if args.skip_existing and out_path.exists():
            skipped += 1
            continue

        url = streetview_url(wp["lat"], wp["lon"], wp["heading"], wp.get("pitch", 0), wp.get("fov", 90))

        try:
            driver.get(url)
            time.sleep(args.step_delay)  # esperar a que cargue Street View

            # Esperar a que se renderice el canvas de Street View
            time.sleep(1)

            # Capturar screenshot
            driver.save_screenshot(str(out_path))

            # Verificar que no sea imagen vacía/error
            if out_path.stat().st_size > 50000:  # >50KB = hay imagen real
                captured += 1
            else:
                out_path.unlink()  # borrar capturas vacías
                failed += 1

        except Exception as e:
            failed += 1

        # Progreso
        if (i + 1) % 10 == 0 or i == 0:
            total = len(waypoints)
            pct = (i + 1) / total * 100
            eta_s = (total - i - 1) * (args.step_delay + 1.5)
            eta_m = eta_s / 60
            print(f"  [{pct:5.1f}%] {i+1}/{total} — OK:{captured} FAIL:{failed} SKIP:{skipped} — ETA:{eta_m:.0f}min")

    driver.quit()
    return captured

def main():
    p = argparse.ArgumentParser(description="Dron Virtual Street View (Browser)")
    p.add_argument("--visible",       action="store_true", help="Mostrar navegador (no headless)")
    p.add_argument("--limit",         type=int, default=0, help="Máximo capturas (0=todas)")
    p.add_argument("--zona",          type=str, help="Solo waypoints de esta zona")
    p.add_argument("--step-delay",    type=float, default=2.5, help="Segundos entre capturas")
    p.add_argument("--skip-existing", action="store_true", default=True)
    args = p.parse_args()

    print("=" * 60)
    print("  DRON VIRTUAL STREET VIEW (BROWSER) — GRATIS")
    print("=" * 60)

    if not WAYPOINTS_JSON.exists():
        print("Primero genera el plan de vuelo:")
        print("  python Tools/drone_streetview_capture.py --plan-only")
        return

    with open(WAYPOINTS_JSON, encoding="utf-8") as f:
        data = json.load(f)

    waypoints = data.get("waypoints", [])
    print(f"Waypoints cargados: {len(waypoints)}")
    print(f"Cobertura objetivo: {data.get('coverage_pct', '?')}% del pueblo")

    # Filtrar existentes
    existing = set()
    if AAA_DIR.exists():
        for f in AAA_DIR.iterdir():
            if f.name.endswith("_albedo.png"):
                existing.add(f.stem.replace("_albedo", ""))
    if OUTPUT_DIR.exists():
        for f in OUTPUT_DIR.iterdir():
            if f.suffix in (".png", ".jpg"):
                existing.add(f.stem)

    # Deduplicar: 1 por edificio, el más cercano
    best = {}
    for wp in waypoints:
        bid = wp["building_id"]
        if bid in existing: continue
        if args.zona:
            # Filtrar por zona
            from Tools.drone_streetview_capture import ZONA_CENTERS_LONLAT
            zlon, zlat = ZONA_CENTERS_LONLAT.get(args.zona, (0, 0))
            d = math.sqrt((wp["lat"]-zlat)**2 + (wp["lon"]-zlon)**2) * 111320
            if d > 200: continue
        if bid not in best or wp["distance_m"] < best[bid]["distance_m"]:
            best[bid] = wp

    to_capture = list(best.values())
    if args.limit > 0:
        to_capture = to_capture[:args.limit]

    print(f"Edificios ya cubiertos: {len(existing)}")
    print(f"A capturar ahora: {len(to_capture)}")
    eta = len(to_capture) * (args.step_delay + 1.5) / 60
    print(f"Tiempo estimado: {eta:.0f} minutos ({args.step_delay}s/captura)")

    if not to_capture:
        print("Todos los edificios ya tienen textura.")
        return

    print()
    captured = run_browser_drone(to_capture, args)

    total = len(existing) + captured
    print(f"\nCobertura final: {total}/{data.get('buildings_total', '?')} edificios")

    with open(REPORT_JSON, "w", encoding="utf-8") as f:
        json.dump({
            "capturas_nuevas": captured,
            "preexistentes": len(existing),
            "total_cubiertos": total,
        }, f, indent=2)

if __name__ == "__main__":
    main()
