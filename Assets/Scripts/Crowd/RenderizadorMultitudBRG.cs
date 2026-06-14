// Assets/Scripts/Crowd/RenderizadorMultitudBRG.cs
// ═══════════════════════════════════════════════════════════════════════════
//  RENDERIZADOR DE MULTITUD — BatchRendererGroup (BRG)
//
//  Sube las matrices TRS nativas (float3×4) directamente a un GraphicsBuffer y
//  emite UN solo draw command para las N instancias. Sin GameObjects, sin
//  MaterialPropertyBlock, sin el tope de 1023 de Graphics.DrawMeshInstanced.
//
//  ── LAYOUT DEL GraphicsBuffer (Target.Raw, stride 4) ─────────────────────────
//    [0      , 96)            cabecera: matriz cero (índice 0 "seguro")
//    [96     , 96+48N)        objectToWorld  (PackedMatrix 48 B × N)
//    [96+48N , 96+96N)        worldToObject  (PackedMatrix 48 B × N)
//    [96+96N , 96+112N)       _BaseColor     (float4 16 B × N)
//    La cabecera de 96 B hace que cada dirección sea múltiplo exacto del tamaño
//    de su tipo (48 y 16) — requisito de GraphicsBuffer.SetData<T> (el índice de
//    destino se mide en unidades de sizeof(T), no del stride del buffer).
//
//  ── REQUISITO DE SHADER ──────────────────────────────────────────────────────
//    El material debe usar un shader compatible con DOTS Instancing (HDRP/Lit lo
//    es). El BRG selecciona automáticamente la variante DOTS_INSTANCING_ON y lee
//    unity_ObjectToWorld / unity_WorldToObject / _BaseColor por-instancia desde
//    el buffer vía los MetadataValue (bit 0x80000000 = "dato por instancia").
//
//  ── PLATAFORMA ───────────────────────────────────────────────────────────────
//    Usa GraphicsBuffer.Target.Raw (DX11/DX12/Vulkan/Metal — el target del
//    proyecto, Windows). En plataformas que sólo soportan constant buffers
//    (algún GLES) habría que trocear en ventanas; no aplica aquí.
// ═══════════════════════════════════════════════════════════════════════════

using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Alsasua.Crowd
{
    public sealed class RenderizadorMultitudBRG : IDisposable
    {
        const int kBytesCabecera = 96;                 // múltiplo de 48 y de 16
        const int kSizePacked    = 12 * sizeof(float); // 48 B  (PackedMatrix)
        const int kSizeFloat4    = 4  * sizeof(float); // 16 B  (color)

        BatchRendererGroup _brg;
        GraphicsBuffer     _instData;
        BatchID            _batchID;
        BatchMeshID        _meshID;
        BatchMaterialID    _matID;

        readonly int  _n;
        readonly uint _addrO2W, _addrW2O, _addrColor;
        readonly float _radio;              // radio de la esfera envolvente por instancia (culling)
        bool _batchCreado;

        // Referencia NO-propietaria a las matrices del frame (las posee el orquestador).
        // Sirve para leer el centro de cada instancia en el culling CPU. Estable durante
        // el render: los jobs ya se completaron en LateUpdate antes de subirlas.
        NativeArray<PackedMatrix> _o2wCPU;

        public RenderizadorMultitudBRG(Mesh mesh, Material material, int numInstancias,
                                       NativeArray<float4> colores, float radioInstancia)
        {
            _n = numInstancias;
            _radio = radioInstancia;

            _addrO2W   = (uint)kBytesCabecera;
            _addrW2O   = _addrO2W + (uint)(kSizePacked * _n);
            _addrColor = _addrW2O + (uint)(kSizePacked * _n);
            int totalBytes = kBytesCabecera + (kSizePacked * 2 + kSizeFloat4) * _n;

            _brg    = new BatchRendererGroup(OnPerformCulling, IntPtr.Zero);
            _meshID = _brg.RegisterMesh(mesh);
            _matID  = _brg.RegisterMaterial(material);

            // GraphicsBuffer crudo (stride 4). totalBytes es múltiplo de 4 por construcción.
            _instData = new GraphicsBuffer(GraphicsBuffer.Target.Raw, totalBytes / sizeof(int), sizeof(int));

            // Cabecera: matriz cero en el índice 0 (64 B; el resto de los 96 es padding).
            _instData.SetData(new[] { Matrix4x4.zero }, 0, 0, 1);
            // Colores: estáticos, se suben una sola vez.
            _instData.SetData(colores, 0, (int)(_addrColor / kSizeFloat4), _n);

            var metadata = new NativeArray<MetadataValue>(3, Allocator.Temp);
            metadata[0] = new MetadataValue { NameID = Shader.PropertyToID("unity_ObjectToWorld"), Value = 0x80000000 | _addrO2W };
            metadata[1] = new MetadataValue { NameID = Shader.PropertyToID("unity_WorldToObject"), Value = 0x80000000 | _addrW2O };
            metadata[2] = new MetadataValue { NameID = Shader.PropertyToID("_BaseColor"),          Value = 0x80000000 | _addrColor };
            _batchID = _brg.AddBatch(metadata, _instData.bufferHandle);
            metadata.Dispose();
            _batchCreado = true;

            // Bounds globales generosos: la multitud es local, no merece la pena
            // recalcularlos por frame; con esto el BRG nunca descarta el lote entero.
            _brg.SetGlobalBounds(new Bounds(Vector3.zero, Vector3.one * 100000f));
        }

        /// <summary>Re-sube el buffer de colores por-instancia (modo depuración:
        /// teñir la multitud por opinión y restaurar la paleta al salir).</summary>
        public void SubirColores(NativeArray<float4> colores)
        {
            _instData.SetData(colores, 0, (int)(_addrColor / kSizeFloat4), _n);
        }

        /// <summary>Sube las matrices del frame (las construye BuildMatricesJob).</summary>
        public void SubirMatrices(NativeArray<PackedMatrix> objectToWorld,
                                  NativeArray<PackedMatrix> worldToObject)
        {
            // graphicsBufferStartIndex en unidades de sizeof(PackedMatrix)=48.
            _instData.SetData(objectToWorld, 0, (int)(_addrO2W / kSizePacked), _n);
            _instData.SetData(worldToObject, 0, (int)(_addrW2O / kSizePacked), _n);
            _o2wCPU = objectToWorld;   // referencia para el culling (la traslación va en c3)
        }

        // ── Callback de culling: rellena los draw commands con punteros crudos ──
        unsafe JobHandle OnPerformCulling(BatchRendererGroup rendererGroup,
                                          BatchCullingContext cullingContext,
                                          BatchCullingOutput cullingOutput,
                                          IntPtr userContext)
        {
            int align = UnsafeUtility.AlignOf<long>();
            var cmds = (BatchCullingOutputDrawCommands*)cullingOutput.drawCommands.GetUnsafePtr();

            cmds->drawCommands     = (BatchDrawCommand*)UnsafeUtility.Malloc(UnsafeUtility.SizeOf<BatchDrawCommand>(), align, Allocator.TempJob);
            cmds->drawRanges       = (BatchDrawRange*)UnsafeUtility.Malloc(UnsafeUtility.SizeOf<BatchDrawRange>(), align, Allocator.TempJob);
            cmds->visibleInstances = (int*)UnsafeUtility.Malloc(_n * sizeof(int), align, Allocator.TempJob);
            cmds->drawCommandPickingInstanceIDs = null;
            cmds->instanceSortingPositions      = null;
            cmds->instanceSortingPositionFloatCount = 0;

            // ── Visibilidad ──────────────────────────────────────────────────
            //  · Pase de CÁMARA → frustum culling CPU (el gran ahorro al panear).
            //  · Pases de LUZ/sombra → se dibujan todas: hacer split-culling mal
            //    abre huecos en las cascadas de sombra. Correctitud > microopt.
            int vis = 0;
            bool cullCamara = cullingContext.viewType == BatchCullingViewType.Camera && _o2wCPU.IsCreated;

            if (cullCamara)
            {
                var planos = cullingContext.cullingPlanes;
                int np = planos.Length;
                for (int i = 0; i < _n; i++)
                {
                    PackedMatrix m = _o2wCPU[i];               // traslación = (c3x, c3y, c3z)
                    bool dentro = true;
                    for (int p = 0; p < np; p++)
                    {
                        Plane pl = planos[p];
                        // distancia firmada al plano; fuera si < -radio en CUALQUIER plano.
                        if (pl.normal.x * m.c3x + pl.normal.y * m.c3y + pl.normal.z * m.c3z + pl.distance < -_radio)
                        { dentro = false; break; }
                    }
                    if (dentro) cmds->visibleInstances[vis++] = i;
                }
            }
            else
            {
                for (int i = 0; i < _n; i++) cmds->visibleInstances[i] = i;
                vis = _n;
            }

            cmds->visibleInstanceCount = vis;
            cmds->drawCommandCount = vis > 0 ? 1 : 0;
            cmds->drawRangeCount   = vis > 0 ? 1 : 0;
            if (vis == 0) return new JobHandle();   // nada visible en este pase

            cmds->drawCommands[0] = new BatchDrawCommand
            {
                visibleOffset       = 0,
                visibleCount        = (uint)vis,
                batchID             = _batchID,
                materialID          = _matID,
                meshID              = _meshID,
                submeshIndex        = 0,
                splitVisibilityMask = 0xff,
                flags               = BatchDrawCommandFlags.None,
                sortingPosition     = 0
            };

            cmds->drawRanges[0] = new BatchDrawRange
            {
                drawCommandsBegin = 0,
                drawCommandsCount = 1,
                filterSettings = new BatchFilterSettings
                {
                    renderingLayerMask = 0xffffffff,
                    layer              = 0,
                    motionMode         = MotionVectorGenerationMode.Camera,
                    shadowCastingMode  = ShadowCastingMode.On,
                    receiveShadows     = true,
                }
            };

            // Sin job de culling propio: el trabajo ya está hecho de forma síncrona.
            return new JobHandle();
        }

        public void Dispose()
        {
            if (_brg != null)
            {
                if (_batchCreado) _brg.RemoveBatch(_batchID);
                _brg.Dispose();
                _brg = null;
            }
            _instData?.Dispose();
            _instData = null;
        }
    }
}
