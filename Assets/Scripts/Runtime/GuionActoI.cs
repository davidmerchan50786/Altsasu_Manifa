// Assets/Scripts/Runtime/GuionActoI.cs
// ═══════════════════════════════════════════════════════════════════════════
//  GUION — ACTO I "El forastero" (M00–M04)
//
//  Conversaciones del Acto I montadas como ConversacionDialogo en código,
//  listas para el motor SistemaDialogo. Tono: cine negro + humor negro.
//  FICCIÓN — valle navarro inventado; personajes y sucesos ficticios.
//
//  Uso:
//    SistemaDialogo.I.Iniciar(GuionActoI.M02_Amaia());
//    SistemaDialogo.AlEvento += e => SistemaApoyoPopular.Instance?.Procesar(e);
//
//  Eventos que emiten estas conversaciones (que escuche el sistema de misiones
//  / apoyo popular):
//    "subir_apoyo", "bajar_apoyo", "pista_pendrive", "aliado_amaia",
//    "rechazo_amaia", "objetivo_fabrica", "objetivo_pleno", "manu_se_queda"
//
//  Capa RUNTIME.
// ═══════════════════════════════════════════════════════════════════════════
using UnityEngine;

public static class GuionActoI
{
    static ConversacionDialogo Conv(string inicial, params NodoDialogo[] nodos)
    {
        var c = ScriptableObject.CreateInstance<ConversacionDialogo>();
        c.nodoInicial = inicial;
        c.nodos = nodos;
        return c;
    }

    static NodoDialogo N(string id, string hablante, string texto,
                         string siguiente = null, string evento = null,
                         params OpcionDialogo[] opciones)
        => new NodoDialogo { id = id, hablante = hablante, texto = texto,
                             siguiente = siguiente, evento = evento, opciones = opciones };

    static OpcionDialogo O(string texto, string destino, string evento = null)
        => new OpcionDialogo { texto = texto, destino = destino, evento = evento };

    // ─────────────────────────────────────────────────────────────────────
    //  M00 · ESNATU, ALTSASU  (llegada de noche, bar de Patxi)
    // ─────────────────────────────────────────────────────────────────────
    public static ConversacionDialogo M00_Llegada() => Conv("inicio",
        N("inicio", "Patxi",
          "Vaya, vaya. El hijo pródigo. Nueve años y entras como si hubieras salido a por tabaco.",
          siguiente: "manu1"),
        N("manu1", "Manu",
          "Hola, Patxi. Ponme algo que queme.",
          opciones: new[]{
            O("¿Sabes lo de mi hermano?", "joseba"),
            O("No he venido a hablar. Solo a beber.", "beber")
          }),
        N("joseba", "Patxi",
          "Todo el pueblo sabe lo de Joseba. Y todo el pueblo se ha creído lo del accidente. Menos dos.",
          opciones: new[]{
            O("¿Quiénes son los dos?", "dos"),
            O("Yo no me lo creo. Ese es uno.", "dos")
          }),
        N("dos", "Patxi",
          "Tú. Y la chica del semanario, Amaia. Los demás prefieren dormir tranquilos. Aquí dormir tranquilo es un lujo, chaval.",
          siguiente: "cierre", evento: "aliado_amaia"),
        N("beber", "Patxi",
          "Como quieras. Pero el primero lo pago yo. Por Joseba. Era mejor que tú... y eso no es decir mucho.",
          siguiente: "cierre"),
        N("cierre", "Manu",
          "(El whisky sabe a tubería vieja. Fuera, un coche patrulla pasa despacio, sin prisa, como quien cuenta ganado.)",
          evento: "objetivo_fabrica")
    );

    // ─────────────────────────────────────────────────────────────────────
    //  M01 · LA FACTURA DEL ACCIDENTE  (fábrica, seguridad privada)
    // ─────────────────────────────────────────────────────────────────────
    public static ConversacionDialogo M01_Fabrica() => Conv("inicio",
        N("inicio", "Guardia",
          "Esto es propiedad privada. Las pertenencias del fallecido las gestiona la empresa.",
          opciones: new[]{
            O("Eran de mi hermano. Me las llevo.", "tenso"),
            O("Solo quiero su taquilla. Cinco minutos.", "taquilla")
          }),
        N("tenso", "Guardia",
          "No me obligues a llamar a nadie, ¿vale? Hoy ha sido un día largo y tú tienes pinta de alargarlo.",
          opciones: new[]{
            O("Pues llama. Yo espero.", "espera", "bajar_apoyo"),
            O("(Retirarse y entrar de noche)", "noche")
          }),
        N("taquilla", "Guardia",
          "...Cinco minutos. Y no he visto nada. Date prisa, que las cámaras de aquí graban hasta lo que pienso.",
          siguiente: "pendrive"),
        N("espera", "Manu",
          "(El guardia no llama a nadie. La gente que de verdad manda no contesta al teléfono de un vigilante.)",
          siguiente: "pendrive"),
        N("noche", "Manu",
          "(Mejor cuando no haya nadie. Algunas verdades solo salen con la persiana bajada.)",
          siguiente: "pendrive"),
        N("pendrive", "Manu",
          "(En el doble fondo de la taquilla, pegado con cinta: un pendrive. Joseba siempre escondía las cosas donde nadie mira: a plena vista.)",
          evento: "pista_pendrive")
    );

    // ─────────────────────────────────────────────────────────────────────
    //  M02 · LO QUE AMAIA NO PUBLICA  (la periodista; primer apoyo)
    // ─────────────────────────────────────────────────────────────────────
    public static ConversacionDialogo M02_Amaia() => Conv("inicio",
        N("inicio", "Amaia",
          "Así que tú eres el otro que no duerme. Patxi me avisó. ¿Qué traes?",
          opciones: new[]{
            O("Un pendrive de Joseba. No sé abrirlo.", "pendrive"),
            O("Preguntas. Muchas.", "preguntas")
          }),
        N("pendrive", "Amaia",
          "Está cifrado. Tu hermano era cuidadoso. Yo puedo intentarlo... pero esto, si es lo que parece, no es un reportaje. Es una diana.",
          siguiente: "trato"),
        N("preguntas", "Amaia",
          "Las preguntas aquí se pagan caras y se cobran a plazos. Yo llevo tres años pagando. ¿Seguro que quieres abrir cuenta?",
          siguiente: "trato"),
        N("trato", "Amaia",
          "Te propongo algo. Necesito que cuatro personas hablen conmigo, con nombres. Solas no se atreven. Si tú estás delante, igual sí.",
          opciones: new[]{
            O("Cuenta conmigo. Los traigo.", "acepta", "aliado_amaia"),
            O("¿Y si los pongo en peligro?", "duda"),
            O("No soy tu recadero.", "rechaza")
          }),
        N("acepta", "Amaia",
          "Entonces empezamos. Y, Manu... gracias. Hacía tiempo que nadie decía 'cuenta conmigo' en este pueblo sin pedir vuelta.",
          evento: "subir_apoyo"),
        N("duda", "Amaia",
          "Ya lo están. Todos. La diferencia es que tú les vas a dar una razón para dejar de estarlo en silencio.",
          siguiente: "acepta"),
        N("rechaza", "Amaia",
          "Vale. Pero el pendrive lo dejas aquí. Tú quieres venganza; yo quiero que esto sirva de algo. No es lo mismo, aunque esta noche lo parezca.",
          evento: "rechazo_amaia")
    );

    // ─────────────────────────────────────────────────────────────────────
    //  M03 · EL PLENO  (comedia negra: la trama bajo el orden del día)
    // ─────────────────────────────────────────────────────────────────────
    public static ConversacionDialogo M03_Pleno() => Conv("inicio",
        N("inicio", "Alcalde Rekalde",
          "Punto siete del orden del día: la rotonda de la entrada. Punto ocho: el futuro de la fábrica y de vuestros hijos. Por ese orden, que la rotonda urge más.",
          siguiente: "manu1"),
        N("manu1", "Manu",
          "(Sonríe a todo el mundo. Estrecha manos como quien firma sentencias. Y nadie, nadie, le mira a los ojos.)",
          opciones: new[]{
            O("Levantarse y preguntar por la caja de la fábrica", "pregunta", "bajar_apoyo"),
            O("Callar y escuchar el nombre que va a soltar", "escucha")
          }),
        N("pregunta", "Alcalde Rekalde",
          "El señor... Garralda, ¿verdad? Mi más sentido pésame. Las cuentas de una empresa privada no se debaten en un pleno. Pero le invito a un café. Los vivos hay que cuidarlos.",
          siguiente: "amenaza"),
        N("escucha", "Alcalde Rekalde",
          "El inversor que va a salvar esto es serio. Internacional. Le llaman 'el Francés'. Un caballero. Trae empleo y se lleva muy poco a cambio. Casi nada. Solo... tranquilidad.",
          siguiente: "amenaza", evento: "pista_pendrive"),
        N("amenaza", "Manu",
          "(El Alcalde no amenaza. Te ofrece café. En este valle, un café del Alcalde es lo más parecido a una esquela con tu nombre.)",
          evento: "objetivo_pleno")
    );

    // ─────────────────────────────────────────────────────────────────────
    //  M04 · SANGRE VIEJA  (la paliza de aviso; Manu decide quedarse)
    // ─────────────────────────────────────────────────────────────────────
    public static ConversacionDialogo M04_Decision() => Conv("inicio",
        N("inicio", "Manu",
          "(Tres costillas, un labio partido y un mensaje muy claro: 'vete'. Lo gracioso es que han elegido la única frase que garantiza que me quedo.)",
          siguiente: "amaia1"),
        N("amaia1", "Amaia",
          "Te dije que era una diana. Aún estás a tiempo de coger el coche. Nadie te lo echaría en cara.",
          opciones: new[]{
            O("Me quedo. Esto ya es personal.", "queda_venganza"),
            O("Me quedo. Pero lo hacemos bien, con el pueblo.", "queda_pueblo"),
            O("Tienes razón. Esto me supera.", "se_va")
          }),
        N("queda_venganza", "Amaia",
          "Vale. Pero la venganza es un agujero, Manu. Cabe entero el que la cava. Aun así... no te vas a meter solo. Faltaría más.",
          evento: "manu_se_queda"),
        N("queda_pueblo", "Amaia",
          "Esa es la respuesta que habría dado Joseba. Y por una vez en tu vida, ser como tu hermano es buena idea. Vamos a despertar a Altsasu.",
          evento: "subir_apoyo"),
        N("se_va", "Amaia",
          "(Manu mira las llaves del coche un rato largo. Luego las deja en la mesa. Algunos no saben irse; por eso vuelven.)",
          siguiente: "queda_pueblo")
    );
}
