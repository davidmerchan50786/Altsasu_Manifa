// AlsasuaManifa.h
// ═══════════════════════════════════════════════════════════════════════════
//  Cabecera del módulo primario de juego AlsasuaManifa.
// ═══════════════════════════════════════════════════════════════════════════

#pragma once

#include "CoreMinimal.h"
#include "Modules/ModuleManager.h"

// Módulo primario del juego. La macro IMPLEMENT_PRIMARY_GAME_MODULE en el .cpp
// lo registra como punto de entrada del ejecutable.
class FAlsasuaManifaModule : public IModuleInterface
{
public:
    virtual void StartupModule() override;
    virtual void ShutdownModule() override;
};
