# FacadeTextures / StreetView

Coloca aquí las fotos reales de fachadas de Alsasua organizadas en subcarpetas por zona:

  casco_viejo/
  plaza_fueros/
  ferial/
  gaztetxe/
  iglesia/
  ayto/
  plaza_zubeztia/
  calle_garcia_jimenez/

Formatos soportados: .jpg  .jpeg  .png

Las fotos deben ser en orientación horizontal (landscape).
El script process_streetview_photos.py extraerá texturas procesadas en:
  Assets/AlsasuaData/FacadeTextures/Processed/

Para ejecutar el procesado:
  python3 Tools/process_streetview_photos.py
