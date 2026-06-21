// Assets/Scripts/SistemaDialogos.cs
using UnityEngine;
using System.Collections.Generic;

[AddComponentMenu("Alsasua V8/Diálogos Ramificados (Humor Ácido)")]
public class SistemaDialogos : MonoBehaviour
{
    private class NodoDialogo
    {
        public string textoNPC;
        public string opcion1;
        public string respuesta1;
        public string opcion2;
        public string respuesta2;
    }

    private class NPCInteractivo
    {
        public GameObject modelo;
        public string nombre;
        public NodoDialogo nodoOriginal;
        public NodoDialogo nodoActual;
    }

    private List<NPCInteractivo> catalogoNPCs = new List<NPCInteractivo>();
    private NPCInteractivo npcActivo = null;
    
    private enum EstadoCharla { Buscando, LeyendoPrincipal, LeyendoRespuesta }
    private EstadoCharla estado = EstadoCharla.Buscando;

    private void Start()
    {
        GenerarNPCsConLore();
    }

    private void GenerarNPCsConLore()
    {
        var nodos = new List<NodoDialogo>()
        {
            new NodoDialogo {
                textoNPC = "Eh, fiera. ¿Has visto cómo huele el río Alzania hoy? Algunos dicen que los de uniforme tiran ahí la mercancía cuando hay redada en el cuartel.",
                opcion1 = "[1] ¿Qué mercancía? ¿Heroína?", respuesta1 = "Heroína, fardos sospechosos... Aquí todo el mundo mira al monte para no ver qué pasa en el cuartel. Los furgones van cargaditos a medianoche.",
                opcion2 = "[2] Seguro que son imaginaciones tuyas nacionalistas.", respuesta2 = "Eso dicen en Madrid hasta que las ratas del canal mutan. Date una vuelta por la zona industrial si tienes lo que hay que tener."
            },
            new NodoDialogo {
                textoNPC = "Desde lo de Operación Ogro, cada vez que veo un todoterreno de la Benemérita me espero que salga volando por encima del campanario. Humor negro local.",
                opcion1 = "[1] Vaya sentido del humor más macabro gastáis por Alsasua.", respuesta1 = "Si no nos reímos del conflicto, nos devora el cinismo. Aquí aprendes a mirar bajo el chasis del coche antes de arrancar, rutina matutina.",
                opcion2 = "[2] Eso es pura apología a la destrucción urbana.", respuesta2 = "Apología es cómo nos tratan en los telediarios. Somos el parque temático del conflicto político, colega. Todo arde tarde o temprano."
            },
            new NodoDialogo {
                textoNPC = "Llevo tres horas de guardia oscura en esta esquina. Juro que he visto drones militares espiando a los punks que se pinchan al fondo de la calle.",
                opcion1 = "[1] Serán drones antidisturbios de la Ertzaintza.", respuesta1 = "Vigilancia para qué, si ya saben quién mueve la droga y quién la compra. Vienen a asegurarse de que el ecosistema marginal siga su curso.",
                opcion2 = "[2] Tienes que dejar de juntarte con los punks del callejón.", respuesta2 = "Ellos son los únicos que ven la verdad. El resto está tragándose la propaganda. Vigila el cielo."
            }
        };

        string[] nombres = { "Lugareño Conspiranoico", "Veterano del Conflicto", "Guardián de la Noche" };

        for (int i = 0; i < 3; i++)
        {
            GameObject npc = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            npc.name = "NPC_Dialogo_" + nombres[i];
            npc.transform.position = new Vector3(Random.Range(-80f, 80f), 550f, Random.Range(-80f, 80f));
            npc.GetComponent<Renderer>().material.color = new Color(0.1f, 0.3f, 0.4f); // Azul grisáceo urbano
            npc.AddComponent<GravedadCalles>(); 

            catalogoNPCs.Add(new NPCInteractivo { 
                modelo = npc, 
                nombre = nombres[i], 
                nodoOriginal = nodos[i],
                nodoActual = new NodoDialogo {
                    textoNPC = nodos[i].textoNPC,
                    opcion1 = nodos[i].opcion1, respuesta1 = nodos[i].respuesta1,
                    opcion2 = nodos[i].opcion2, respuesta2 = nodos[i].respuesta2
                }
            });
        }
    }

    private void Update()
    {
        if (Camera.main == null) return;

        if (estado == EstadoCharla.Buscando)
        {
            bool cercaDeAlguien = false;
            foreach (var npc in catalogoNPCs)
            {
                if (Vector3.Distance(Camera.main.transform.position, npc.modelo.transform.position) < 30f)
                {
                    cercaDeAlguien = true;
                    npcActivo = npc;
                    
                    if (Input.GetKeyDown(KeyCode.E))
                    {
                        // Resetear texto al original al volver a hablar
                        npcActivo.nodoActual.textoNPC = npcActivo.nodoOriginal.textoNPC;
                        estado = EstadoCharla.LeyendoPrincipal;
                    }
                    break;
                }
            }
            if (!cercaDeAlguien) npcActivo = null;
        }
        else if (estado == EstadoCharla.LeyendoPrincipal)
        {
            if (Input.GetKeyDown(KeyCode.Alpha1)) { estado = EstadoCharla.LeyendoRespuesta; npcActivo.nodoActual.textoNPC = npcActivo.nodoOriginal.respuesta1; }
            if (Input.GetKeyDown(KeyCode.Alpha2)) { estado = EstadoCharla.LeyendoRespuesta; npcActivo.nodoActual.textoNPC = npcActivo.nodoOriginal.respuesta2; }
            if (Input.GetKeyDown(KeyCode.Alpha0)) { estado = EstadoCharla.Buscando; }
        }
        else if (estado == EstadoCharla.LeyendoRespuesta)
        {
            if (Input.GetKeyDown(KeyCode.Alpha0)) { estado = EstadoCharla.Buscando; }
        }
    }

    private void OnGUI()
    {
        if (npcActivo != null)
        {
            GUI.color = new Color(1f, 1f, 1f, 0.95f);
            GUIStyle stPanel = new GUIStyle(GUI.skin.box);
            
            GUIStyle stTexto = new GUIStyle(GUI.skin.label);
            stTexto.fontSize = 22;
            stTexto.fontStyle = FontStyle.Bold;
            stTexto.wordWrap = true;
            stTexto.normal.textColor = Color.yellow;
            
            Rect panel = new Rect(Screen.width * 0.1f, Screen.height - 240, Screen.width * 0.8f, 220);
            GUI.Box(panel, "", stPanel);
            
            if (estado == EstadoCharla.Buscando)
            {
                GUI.Label(new Rect(panel.x + 20, panel.y + 80, panel.width - 40, 100), $"[E] Hablar con {npcActivo.nombre}", stTexto);
            }
            else if (estado == EstadoCharla.LeyendoPrincipal)
            {
                stTexto.normal.textColor = Color.white;
                GUI.Label(new Rect(panel.x + 20, panel.y + 10, panel.width - 40, 90), $"{npcActivo.nombre}: \"{npcActivo.nodoActual.textoNPC}\"", stTexto);
                
                stTexto.normal.textColor = new Color(0.4f, 0.8f, 1f);
                GUI.Label(new Rect(panel.x + 20, panel.y + 110, panel.width - 40, 30), npcActivo.nodoOriginal.opcion1, stTexto);
                GUI.Label(new Rect(panel.x + 20, panel.y + 140, panel.width - 40, 30), npcActivo.nodoOriginal.opcion2, stTexto);
                
                stTexto.normal.textColor = Color.red;
                GUI.Label(new Rect(panel.x + 20, panel.y + 180, panel.width - 40, 30), "[0] Ignorar y abandonar la charla.", stTexto);
            }
            else if (estado == EstadoCharla.LeyendoRespuesta)
            {
                stTexto.normal.textColor = Color.white;
                GUI.Label(new Rect(panel.x + 20, panel.y + 10, panel.width - 40, 120), $"{npcActivo.nombre}: \"{npcActivo.nodoActual.textoNPC}\"", stTexto);
                
                stTexto.normal.textColor = Color.red;
                GUI.Label(new Rect(panel.x + 20, panel.y + 180, panel.width - 40, 30), "[0] Fin de la conversación.", stTexto);
            }
        }
    }
}
