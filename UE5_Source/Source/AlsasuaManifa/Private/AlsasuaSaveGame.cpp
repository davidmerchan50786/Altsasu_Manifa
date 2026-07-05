// AlsasuaSaveGame.cpp
// ═══════════════════════════════════════════════════════════════════════════
//  Implementación del guardado/carga de partida (UE 5.4).
// ═══════════════════════════════════════════════════════════════════════════

#include "AlsasuaSaveGame.h"
#include "AlsasuaCharacter.h"
#include "Kismet/GameplayStatics.h"

bool UAlsasuaSaveGame::GuardarPersonaje(AAlsasuaCharacter* Personaje, const FString& Slot, int32 Usuario)
{
    if (Personaje == nullptr)
        return false;

    // Crear un objeto de guardado y volcar el estado del personaje.
    UAlsasuaSaveGame* Datos = Cast<UAlsasuaSaveGame>(
        UGameplayStatics::CreateSaveGameObject(UAlsasuaSaveGame::StaticClass()));
    if (Datos == nullptr)
        return false;

    Datos->SlotName      = Slot;
    Datos->UserIndex     = Usuario;
    Datos->SavedHealth   = Personaje->GetCurrentHealth();
    Datos->SavedStamina  = Personaje->GetCurrentStamina();
    Datos->SavedLocation = Personaje->GetActorLocation();
    Datos->SavedRotation = Personaje->GetActorRotation();

    // Escribir en disco de forma síncrona.
    return UGameplayStatics::SaveGameToSlot(Datos, Slot, Usuario);
}

bool UAlsasuaSaveGame::CargarPersonaje(AAlsasuaCharacter* Personaje, const FString& Slot, int32 Usuario)
{
    if (Personaje == nullptr)
        return false;

    // Si no existe la ranura, no hay nada que cargar.
    if (!UGameplayStatics::DoesSaveGameExist(Slot, Usuario))
        return false;

    UAlsasuaSaveGame* Datos = Cast<UAlsasuaSaveGame>(
        UGameplayStatics::LoadGameFromSlot(Slot, Usuario));
    if (Datos == nullptr)
        return false;

    // Aplicar el estado leído al personaje.
    Personaje->SetCurrentHealth(Datos->SavedHealth);
    Personaje->SetCurrentStamina(Datos->SavedStamina);
    Personaje->SetActorLocationAndRotation(Datos->SavedLocation, Datos->SavedRotation);
    return true;
}
