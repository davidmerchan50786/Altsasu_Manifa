"""
ConfigurarRenderAAA.py
Configura Lumen + PostProcess para maxima calidad visual en UE5 5.8.
Ejecutar desde el editor: en Output Log escribir:
    py "C:/ruta/al/proyecto/ConfigurarRenderAAA.py"
O desde la consola de Python del editor de UE5.
"""

import unreal

def log(msg):
    unreal.log(f"[RenderAAA] {msg}")

def warn(msg):
    unreal.log_warning(f"[RenderAAA] {msg}")

# ---------------------------------------------------------------------------
# 1. Consola CVars — Lumen, TSR, VSM
# ---------------------------------------------------------------------------
def aplicar_cvars():
    log("Aplicando CVars globales...")
    cvars = {
        # Lumen Global Illumination
        "r.Lumen.Reflections.Allow":                     "1",
        "r.Lumen.GlobalIllumination.Allow":              "1",
        "r.LumenScene.SurfaceCache.MaxMeshSDFTraceDistance": "12000",
        "r.LumenScene.SurfaceCache.TraceMeshSDFs":       "1",
        "r.Lumen.ScreenProbeGather.FullResolutionJitterWidth": "1",
        "r.Lumen.ScreenProbeGather.TracingOctahedronResolution": "8",
        "r.Lumen.FinalGather.Allow":                     "1",
        "r.Lumen.FinalGather.LightingUpdateSpeed":       "1",
        "r.Lumen.FinalGather.MaxUpdateBudgetFraction":   "1",
        "r.Lumen.TranslucencyReflections.FrontLayer.Allow": "1",
        "r.Lumen.TraceMeshSDFs.Allow":                   "1",
        "r.Lumen.MaxTraceDistance":                      "12000",

        # TSR — Temporal Super Resolution quality level 2
        "r.TemporalAA.Algorithm":                        "1",   # 1 = TSR
        "r.TSR.ShadingRejection.Flickering":             "1",
        "r.TSR.ShadingRejection.Flickering.Period":      "3",
        "r.TSR.History.SampleCount":                     "32",
        "r.TSR.Velocity.WeightClampingSampleCount":       "4",

        # Virtual Shadow Maps
        "r.Shadow.Virtual.Enable":                       "1",
        "r.Shadow.Virtual.Cache.StaticSeparate":         "1",
        "r.Shadow.Virtual.Cache.InvalidateOnTranslucencyLighting": "0",
        "r.Shadow.Virtual.ResolutionLodBiasDirectional": "-1",
        "r.Shadow.Virtual.ResolutionLodBiasLocal":       "-1",
        "r.Shadow.Virtual.Cache.MaxMaterialInvalidationAgeBias": "1",
        "r.Shadow.Virtual.Clipmap.FirstLevel":           "6",

        # Nanite para mayor densidad geometrica
        "r.Nanite.MaxPixelsPerEdge":                     "1",
        "r.Nanite.ProjectionScale":                      "1",

        # Sky atmosphere y nubes
        "r.SkyAtmosphere.AerialPerspective.StartDepthKm": "0.1",
        "r.VolumetricCloud.SkyLight.IlluminanceOutSideVolume.Override": "1",
    }

    for cvar, value in cvars.items():
        unreal.SystemLibrary.execute_console_command(
            unreal.EditorLevelLibrary.get_editor_world(),
            f"{cvar} {value}"
        )
    log(f"  {len(cvars)} CVars aplicados.")


# ---------------------------------------------------------------------------
# 2. PostProcessVolume — encontrar o crear
# ---------------------------------------------------------------------------
def obtener_o_crear_ppv():
    world = unreal.EditorLevelLibrary.get_editor_world()
    actores = unreal.EditorLevelLibrary.get_all_level_actors()

    for actor in actores:
        if isinstance(actor, unreal.PostProcessVolume):
            log(f"PostProcessVolume encontrado: {actor.get_name()}")
            return actor

    log("PostProcessVolume NO encontrado — creando uno nuevo...")
    ppv = unreal.EditorLevelLibrary.spawn_actor_from_class(
        unreal.PostProcessVolume,
        unreal.Vector(0, 0, 0),
        unreal.Rotator(0, 0, 0)
    )
    if ppv is None:
        warn("No se pudo crear PostProcessVolume. Verifique permisos de la escena.")
        return None
    log(f"PostProcessVolume creado: {ppv.get_name()}")
    return ppv


# ---------------------------------------------------------------------------
# 3. Configurar PostProcessVolume
# ---------------------------------------------------------------------------
def configurar_ppv(ppv):
    if ppv is None:
        warn("PPV es None — saltando configuracion de postprocess.")
        return

    log("Configurando PostProcessVolume...")

    # Sin limites — afecta a todo el nivel
    ppv.set_editor_property("unbound", True)
    ppv.set_editor_property("priority", 10.0)

    settings = ppv.settings

    # --- Exposicion: histograma, min=0.03, max=8, bias=0 ---
    settings.set_editor_property("auto_exposure_method",
        unreal.AutoExposureMethod.AEM_HISTOGRAM)
    settings.set_editor_property("override_auto_exposure_method", True)

    settings.set_editor_property("auto_exposure_min_brightness", 0.03)
    settings.set_editor_property("override_auto_exposure_min_brightness", True)

    settings.set_editor_property("auto_exposure_max_brightness", 8.0)
    settings.set_editor_property("override_auto_exposure_max_brightness", True)

    settings.set_editor_property("auto_exposure_bias", 0.0)
    settings.set_editor_property("override_auto_exposure_bias", True)

    settings.set_editor_property("auto_exposure_apply_physical_camera_exposure", False)
    settings.set_editor_property("override_auto_exposure_apply_physical_camera_exposure", True)

    # --- Bloom: intensidad 0.675 ---
    settings.set_editor_property("bloom_intensity", 0.675)
    settings.set_editor_property("override_bloom_intensity", True)
    settings.set_editor_property("bloom_method", unreal.BloomMethod.BM_FFT)
    settings.set_editor_property("override_bloom_method", True)

    # --- Vignette: 0.3 ---
    settings.set_editor_property("vignette_intensity", 0.3)
    settings.set_editor_property("override_vignette_intensity", True)

    # --- Lumen GI en el volumen ---
    try:
        settings.set_editor_property("lumen_scene_detail", 1.0)
        settings.set_editor_property("override_lumen_scene_detail", True)
        settings.set_editor_property("lumen_scene_lighting_quality", 1.0)
        settings.set_editor_property("override_lumen_scene_lighting_quality", True)
        settings.set_editor_property("lumen_scene_view_distance", 12000.0)
        settings.set_editor_property("override_lumen_scene_view_distance", True)
        settings.set_editor_property("lumen_final_gather_quality", 2.0)
        settings.set_editor_property("override_lumen_final_gather_quality", True)
        settings.set_editor_property("lumen_final_gather_lighting_update_speed", 1.0)
        settings.set_editor_property("override_lumen_final_gather_lighting_update_speed", True)
        settings.set_editor_property("lumen_reflection_quality", 2.0)
        settings.set_editor_property("override_lumen_reflection_quality", True)
        settings.set_editor_property("lumen_ray_lighting_mode",
            unreal.LumenRayLightingModeOverride.SURFACE_CACHE)
        settings.set_editor_property("override_lumen_ray_lighting_mode", True)
        log("  Lumen configu en PPV: OK")
    except Exception as e:
        warn(f"  Lumen PPV (algunas props pueden no existir en esta version): {e}")

    # --- Ambient Occlusion (complementa Lumen) ---
    try:
        settings.set_editor_property("ambient_occlusion_intensity", 0.5)
        settings.set_editor_property("override_ambient_occlusion_intensity", True)
        settings.set_editor_property("ambient_occlusion_radius", 200.0)
        settings.set_editor_property("override_ambient_occlusion_radius", True)
    except Exception as e:
        warn(f"  AO props: {e}")

    # --- Screen Space Reflections (fallback Lumen) ---
    try:
        settings.set_editor_property("screen_space_reflection_intensity", 100.0)
        settings.set_editor_property("override_screen_space_reflection_intensity", True)
        settings.set_editor_property("screen_space_reflection_quality", 75.0)
        settings.set_editor_property("override_screen_space_reflection_quality", True)
    except Exception as e:
        warn(f"  SSR props: {e}")

    # --- Global Illumination method: Lumen ---
    try:
        settings.set_editor_property("dynamic_global_illumination_method",
            unreal.DynamicGlobalIlluminationMethod.LUMEN)
        settings.set_editor_property("override_dynamic_global_illumination_method", True)
        settings.set_editor_property("reflection_method",
            unreal.ReflectionMethod.LUMEN)
        settings.set_editor_property("override_reflection_method", True)
        log("  GI y Reflection method: Lumen")
    except Exception as e:
        warn(f"  GI/Reflection method: {e}")

    # --- TSR quality level 2 ---
    try:
        settings.set_editor_property("anti_aliasing_method",
            unreal.AntiAliasingMethod.AAM_TSR)
        settings.set_editor_property("override_anti_aliasing_method", True)
        # TSR quality se controla via ScreenPercentage
        settings.set_editor_property("screen_percentage", 100.0)
        settings.set_editor_property("override_screen_percentage", True)
        log("  TSR antialiasing: activado")
    except Exception as e:
        warn(f"  TSR/AA method: {e}")

    # --- Chromatic Aberration minima ---
    try:
        settings.set_editor_property("scene_fringe_intensity", 0.05)
        settings.set_editor_property("override_scene_fringe_intensity", True)
    except Exception as e:
        warn(f"  Chromatic aberration: {e}")

    # Aplicar settings de vuelta
    ppv.set_editor_property("settings", settings)
    log("PostProcessVolume configurado.")


# ---------------------------------------------------------------------------
# 4. Sky Atmosphere — Espana, latitud 43N, aire limpio
# ---------------------------------------------------------------------------
def configurar_sky_atmosphere():
    world = unreal.EditorLevelLibrary.get_editor_world()
    actores = unreal.EditorLevelLibrary.get_all_level_actors()

    sky_atm = None
    for actor in actores:
        if isinstance(actor, unreal.SkyAtmosphere):
            sky_atm = actor
            break

    if sky_atm is None:
        log("SkyAtmosphere NO encontrado — creando...")
        sky_atm = unreal.EditorLevelLibrary.spawn_actor_from_class(
            unreal.SkyAtmosphere,
            unreal.Vector(0, 0, 0),
            unreal.Rotator(0, 0, 0)
        )
        if sky_atm is None:
            warn("No se pudo crear SkyAtmosphere.")
            return
    else:
        log(f"SkyAtmosphere encontrado: {sky_atm.get_name()}")

    # Rayleigh scattering para latitud ~43N Espana — aire limpio
    # Scattering base: 0.0331 km-1 (estandar Rayleigh a nivel del mar)
    # Altitud Alsasua ~530m -> densidad ligeramente reducida
    try:
        sky_atm.set_editor_property("rayleigh_scattering_scale", 0.0331)
        # Color Rayleigh: azul cielo vasco (ligeramente grisaceo en humedo)
        sky_atm.set_editor_property("rayleigh_scattering",
            unreal.LinearColor(0.175, 0.409, 1.0))
        sky_atm.set_editor_property("rayleigh_exponential_distribution", 8.0)

        # Mie — niebla y humedad vasca (media)
        sky_atm.set_editor_property("mie_scattering_scale", 0.003996)
        sky_atm.set_editor_property("mie_scattering",
            unreal.LinearColor(1.0, 1.0, 1.0))
        sky_atm.set_editor_property("mie_absorption_scale", 0.000444)
        sky_atm.set_editor_property("mie_exponential_distribution", 1.2)
        sky_atm.set_editor_property("mie_anisotropy", 0.8)

        # Absorcion (capa ozono) — estandar Europa
        sky_atm.set_editor_property("other_absorption_scale", 1.0)
        sky_atm.set_editor_property("other_absorption",
            unreal.LinearColor(0.65, 1.881, 0.085))

        # Terreno: altura 530m sobre nivel del mar
        sky_atm.set_editor_property("bottom_radius", 6360.0)
        sky_atm.set_editor_property("atmosphere_height", 60.0)
        sky_atm.set_editor_property("ground_albedo",
            unreal.LinearColor(0.4, 0.35, 0.3))

        sky_atm.set_editor_property("aerial_perspective_view_distance_scale", 1.0)
        sky_atm.set_editor_property("height_fog_contribution", 1.0)
        log("SkyAtmosphere configurado (Rayleigh Espana 43N, aire limpio).")
    except Exception as e:
        warn(f"SkyAtmosphere props: {e}")


# ---------------------------------------------------------------------------
# 5. Volumetric Clouds — densidad 0.4, tiempo vasco
# ---------------------------------------------------------------------------
def configurar_volumetric_clouds():
    actores = unreal.EditorLevelLibrary.get_all_level_actors()

    clouds = None
    for actor in actores:
        if isinstance(actor, unreal.VolumetricCloud):
            clouds = actor
            break

    if clouds is None:
        log("VolumetricCloud NO encontrado — creando...")
        clouds = unreal.EditorLevelLibrary.spawn_actor_from_class(
            unreal.VolumetricCloud,
            unreal.Vector(0, 0, 0),
            unreal.Rotator(0, 0, 0)
        )
        if clouds is None:
            warn("No se pudo crear VolumetricCloud.")
            return
    else:
        log(f"VolumetricCloud encontrado: {clouds.get_name()}")

    try:
        clouds.set_editor_property("layer_bottom_altitude", 1.5)   # km
        clouds.set_editor_property("layer_height", 7.0)             # km
        clouds.set_editor_property("tracing_max_distance", 150.0)
        clouds.set_editor_property("tracing_start_max_distance", 350.0)
        clouds.set_editor_property("planet_radius", 6360.0)

        # Densidad 0.4 — tiempo vasco tipico (nubes estratocumulos)
        # La densidad real se controla en el Material del cloud; aqui ajustamos
        # los multipliers de albedo que modulan opacidad efectiva
        clouds.set_editor_property("sky_light_cloud_bottom_occlusion", 0.4)
        clouds.set_editor_property("view_sample_count_scale", 1.0)
        clouds.set_editor_property("shadow_view_sample_count_scale", 1.0)
        clouds.set_editor_property("shadow_reflection_sample_count_scale", 1.0)
        clouds.set_editor_property("shadow_tracing_distance", 15.0)
        clouds.set_editor_property("stop_tracing_transmittance_threshold", 0.5)
        log("VolumetricCloud configurado (densidad 0.4, tiempo vasco).")
    except Exception as e:
        warn(f"VolumetricCloud props: {e}")


# ---------------------------------------------------------------------------
# 6. ExponentialHeightFog — niebla vasca azul-grisacea
# ---------------------------------------------------------------------------
def configurar_height_fog():
    actores = unreal.EditorLevelLibrary.get_all_level_actors()

    fog = None
    for actor in actores:
        if isinstance(actor, unreal.ExponentialHeightFog):
            fog = actor
            break

    if fog is None:
        log("ExponentialHeightFog NO encontrado — creando...")
        fog = unreal.EditorLevelLibrary.spawn_actor_from_class(
            unreal.ExponentialHeightFog,
            unreal.Vector(0, 0, 0),
            unreal.Rotator(0, 0, 0)
        )
        if fog is None:
            warn("No se pudo crear ExponentialHeightFog.")
            return
    else:
        log(f"ExponentialHeightFog encontrado: {fog.get_name()}")

    try:
        comp = fog.get_component_by_class(unreal.ExponentialHeightFogComponent)
        if comp is None:
            warn("ExponentialHeightFogComponent no encontrado en el actor.")
            return

        # Densidad 0.02 — niebla ligera de valle vasco
        comp.set_editor_property("fog_density", 0.02)
        comp.set_editor_property("fog_height_falloff", 0.2)
        comp.set_editor_property("fog_max_opacity", 1.0)
        comp.set_editor_property("start_distance", 100.0)
        comp.set_editor_property("fog_cutoff_distance", 0.0)

        # Color inscatter: azul-grisaceo cielo vasco humedo
        # RGB lineal: cielo a mediodia nublado en Pais Vasco
        fog_color = unreal.LinearColor(0.36, 0.47, 0.62, 1.0)
        comp.set_editor_property("inscattering_color_distance", fog_color)
        comp.set_editor_property("fog_inscattering_luminance", 1.0)

        # Segunda capa (niebla de valle, mas densa y mas baja)
        comp.set_editor_property("second_fog_density", 0.005)
        comp.set_editor_property("second_fog_height_offset", -200.0)
        comp.set_editor_property("second_fog_height_falloff", 0.4)

        # Volumetric fog para rayos de luz
        comp.set_editor_property("volumetric_fog", True)
        comp.set_editor_property("volumetric_fog_scattering_distribution", 0.2)
        comp.set_editor_property("volumetric_fog_static_lighting_scattering_intensity", 1.0)
        comp.set_editor_property("volumetric_fog_albedo",
            unreal.LinearColor(1.0, 1.0, 1.0, 1.0))
        comp.set_editor_property("volumetric_fog_emissive",
            unreal.LinearColor(0.0, 0.0, 0.0, 1.0))
        comp.set_editor_property("volumetric_fog_extinction_scale", 1.0)
        comp.set_editor_property("volumetric_fog_distance", 6000.0)
        comp.set_editor_property("volumetric_fog_start_distance", 0.0)

        log("ExponentialHeightFog configurado (densidad 0.02, azul-grisaceo vasco).")
    except Exception as e:
        warn(f"ExponentialHeightFog props: {e}")


# ---------------------------------------------------------------------------
# 7. DirectionalLight — sol de Espana, 10 lux, 5500K
# ---------------------------------------------------------------------------
def configurar_directional_light():
    actores = unreal.EditorLevelLibrary.get_all_level_actors()

    dl = None
    for actor in actores:
        if isinstance(actor, unreal.DirectionalLight):
            dl = actor
            break

    if dl is None:
        log("DirectionalLight NO encontrado — creando...")
        # Angulo tipico de sol en Alsasua mediodia (~60 grados altura, 180 azimut)
        dl = unreal.EditorLevelLibrary.spawn_actor_from_class(
            unreal.DirectionalLight,
            unreal.Vector(0, 0, 0),
            unreal.Rotator(-60.0, 180.0, 0.0)
        )
        if dl is None:
            warn("No se pudo crear DirectionalLight.")
            return
    else:
        log(f"DirectionalLight encontrado: {dl.get_name()}")

    try:
        comp = dl.get_component_by_class(unreal.DirectionalLightComponent)
        if comp is None:
            warn("DirectionalLightComponent no encontrado.")
            return

        # Intensidad 10 lux — mediodia parcialmente nublado Espana
        comp.set_editor_property("intensity", 10.0)

        # Temperatura 5500K — luz diurna estandar
        comp.set_editor_property("use_temperature", True)
        comp.set_editor_property("temperature", 5500.0)
        comp.set_editor_property("light_color", unreal.Color(255, 255, 255, 255))

        # Sombras
        comp.set_editor_property("cast_shadows", True)
        comp.set_editor_property("cast_static_shadows", True)
        comp.set_editor_property("cast_dynamic_shadows", True)
        comp.set_editor_property("cast_volumetric_shadow", True)
        comp.set_editor_property("shadow_amount", 1.0)

        # Distancia de sombra — suficiente para ciudad 14km
        comp.set_editor_property("dynamic_shadow_distance_whole_scene", 20000.0)
        comp.set_editor_property("num_dynamic_shadow_cascades", 4)
        comp.set_editor_property("cascade_distribution_exponent", 3.0)
        comp.set_editor_property("cascade_transition_fraction", 0.1)

        # Sol de atmosfera
        comp.set_editor_property("atmosphere_sun_light", True)
        comp.set_editor_property("atmosphere_sun_disk_color_scale",
            unreal.LinearColor(1.0, 1.0, 1.0, 1.0))

        # Lumen trace distance para la luz solar
        try:
            comp.set_editor_property("light_source_angle", 0.5357)   # angulo real del sol
        except Exception:
            pass

        # Soft shadow
        comp.set_editor_property("shadow_num_rays_dim_density", 8)

        log("DirectionalLight configurado (10 lux, 5500K, sombras, atmosfera).")
    except Exception as e:
        warn(f"DirectionalLight props: {e}")


# ---------------------------------------------------------------------------
# 8. SkyLight — captura en tiempo real, 1.0 intensidad
# ---------------------------------------------------------------------------
def configurar_sky_light():
    actores = unreal.EditorLevelLibrary.get_all_level_actors()

    sl = None
    for actor in actores:
        if isinstance(actor, unreal.SkyLight):
            sl = actor
            break

    if sl is None:
        log("SkyLight NO encontrado — creando...")
        sl = unreal.EditorLevelLibrary.spawn_actor_from_class(
            unreal.SkyLight,
            unreal.Vector(0, 0, 0),
            unreal.Rotator(0, 0, 0)
        )
        if sl is None:
            warn("No se pudo crear SkyLight.")
            return
    else:
        log(f"SkyLight encontrado: {sl.get_name()}")

    try:
        comp = sl.get_component_by_class(unreal.SkyLightComponent)
        if comp is None:
            warn("SkyLightComponent no encontrado.")
            return

        # Captura en tiempo real (para dia/noche dinamico)
        comp.set_editor_property("source_type",
            unreal.SkyLightSourceType.SLS_CAPTURED_SCENE)
        comp.set_editor_property("real_time_capture", True)

        comp.set_editor_property("intensity", 1.0)
        comp.set_editor_property("intensity_units",
            unreal.LightUnits.CANDELAS)

        # No sobreexponer con el sol
        comp.set_editor_property("lower_hemisphere_is_solid_color", True)
        comp.set_editor_property("lower_hemisphere_color",
            unreal.LinearColor(0.0, 0.0, 0.0, 1.0))

        comp.set_editor_property("cast_shadows", True)
        comp.set_editor_property("cast_ray_traced_shadow", True)
        comp.set_editor_property("shadow_amount", 1.0)

        # Recapturar
        comp.recapture_sky()
        log("SkyLight configurado (real-time capture, 1.0 intensidad).")
    except Exception as e:
        warn(f"SkyLight props: {e}")


# ---------------------------------------------------------------------------
# 9. TSR quality level 2 via console (refuerzo)
# ---------------------------------------------------------------------------
def configurar_tsr_quality():
    world = unreal.EditorLevelLibrary.get_editor_world()
    # TSR Quality=2 en UE5: r.TSR.History.SampleCount alto + sin ghosting
    cmds = [
        "r.TemporalAA.Algorithm 1",
        "r.TSR.History.SampleCount 32",
        "r.TSR.History.GrandReprojection 1",
        "r.TSR.ShadingRejection.Flickering 1",
        "r.TSR.ShadingRejection.Flickering.Period 3",
        "r.TSR.Velocity.WeightClampingSampleCount 4",
        "r.TSR.AsyncCompute 2",
        "r.TSR.RejectionAntiAliasingQuality 2",
    ]
    for cmd in cmds:
        unreal.SystemLibrary.execute_console_command(world, cmd)
    log("TSR Quality=2 configurado via console.")


# ---------------------------------------------------------------------------
# 10. Virtual Shadow Maps — caching agresivo
# ---------------------------------------------------------------------------
def configurar_vsm():
    world = unreal.EditorLevelLibrary.get_editor_world()
    cmds = [
        "r.Shadow.Virtual.Enable 1",
        "r.Shadow.Virtual.Cache.StaticSeparate 1",
        "r.Shadow.Virtual.NonNanite.IncludeInClipmaps 1",
        "r.Shadow.Virtual.Cache.MaxMaterialInvalidationAgeBias 1",
        "r.Shadow.Virtual.SMRT.RayCountLocal 7",
        "r.Shadow.Virtual.SMRT.RayCountDirectional 7",
        "r.Shadow.Virtual.SMRT.SamplesPerPixelLocal 4",
        "r.Shadow.Virtual.SMRT.SamplesPerPixelDirectional 4",
        "r.Shadow.Virtual.ResolutionLodBiasDirectional -1.0",
        "r.Shadow.Virtual.ResolutionLodBiasLocal -0.5",
        "r.Shadow.Virtual.Cache.InvalidateOnTranslucencyLighting 0",
        "r.Shadow.Virtual.Clipmap.FirstLevel 6",
        "r.Shadow.Virtual.Clipmap.LastLevel 22",
    ]
    for cmd in cmds:
        unreal.SystemLibrary.execute_console_command(world, cmd)
    log("Virtual Shadow Maps configurado (caching agresivo).")


# ---------------------------------------------------------------------------
# 11. Guardar nivel
# ---------------------------------------------------------------------------
def guardar_nivel():
    try:
        world = unreal.EditorLevelLibrary.get_editor_world()
        # Marcar nivel como modificado para forzar guardado
        unreal.EditorLevelLibrary.save_current_level()
        log("Nivel guardado correctamente.")
    except Exception as e:
        warn(f"No se pudo guardar el nivel automaticamente: {e}")
        warn("  -> Guardar manualmente con Ctrl+S o File > Save Current Level")


# ---------------------------------------------------------------------------
# MAIN
# ---------------------------------------------------------------------------
def main():
    log("=" * 60)
    log("ConfigurarRenderAAA — UE5 5.8 — Lumen AAA Quality")
    log("Proyecto: Altsasu Manifa (Alsasua, Navarra, Espana)")
    log("=" * 60)

    # Orden: primero CVars (afectan al engine), luego actores
    aplicar_cvars()
    configurar_tsr_quality()
    configurar_vsm()

    ppv = obtener_o_crear_ppv()
    configurar_ppv(ppv)

    configurar_sky_atmosphere()
    configurar_volumetric_clouds()
    configurar_height_fog()
    configurar_directional_light()
    configurar_sky_light()

    guardar_nivel()

    log("=" * 60)
    log("Configuracion AAA completada.")
    log("Verificar en editor:")
    log("  - PostProcessVolume en Outliner: marcado como Unbound")
    log("  - DirectionalLight: atmosfera = true, 5500K, 10 lux")
    log("  - SkyLight: Real-time capture")
    log("  - Project Settings > Rendering: Lumen GI + VSM activados")
    log("  - Si 0 FPS en Play: StreamerMundoEstatico reduce radio automaticamente")
    log("=" * 60)


# Punto de entrada
if __name__ == "__main__":
    main()
else:
    # Cuando se ejecuta desde la consola de UE5 tambien funciona
    main()
