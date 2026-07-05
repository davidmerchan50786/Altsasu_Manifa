// AlsasuaManifa.cpp
// ═══════════════════════════════════════════════════════════════════════════
//  Implementación y registro del módulo primario de juego.
// ═══════════════════════════════════════════════════════════════════════════

#include "AlsasuaManifa.h"
#include "Modules/ModuleManager.h"

void FAlsasuaManifaModule::StartupModule()
{
    // Inicialización del módulo (se ejecuta al cargar el juego).
    UE_LOG(LogTemp, Log, TEXT("[AlsasuaManifa] Módulo iniciado."));
}

void FAlsasuaManifaModule::ShutdownModule()
{
    // Limpieza del módulo (al descargar).
    UE_LOG(LogTemp, Log, TEXT("[AlsasuaManifa] Módulo detenido."));
}

// Registra AlsasuaManifa como el módulo primario del juego.
IMPLEMENT_PRIMARY_GAME_MODULE(FAlsasuaManifaModule, AlsasuaManifa, "AlsasuaManifa");
