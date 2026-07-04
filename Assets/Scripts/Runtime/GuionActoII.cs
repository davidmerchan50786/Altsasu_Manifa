// Assets/Scripts/Runtime/GuionActoII.cs
// ═══════════════════════════════════════════════════════════════════════════
//  GUION — ACTO II "El pueblo" (M05–M09)
//  Conversaciones para el motor SistemaDialogo. Cine negro + humor negro.
//  FICCIÓN — valle navarro inventado.
//  Eventos: "subir_apoyo","bajar_apoyo","aliada_sara","perder_aliado",
//           "pista_caja_b","manifa_pacifica","manifa_desbordada","muere_patxi"
// ═══════════════════════════════════════════════════════════════════════════
using UnityEngine;

public static class GuionActoII
{
    static ConversacionDialogo Conv(string ini, params NodoDialogo[] n)
    { var c = ScriptableObject.CreateInstance<ConversacionDialogo>(); c.nodoInicial = ini; c.nodos = n; return c; }
    static NodoDialogo N(string id, string h, string t, string siguiente = null, string ev = null, params OpcionDialogo[] opciones)
        => new NodoDialogo { id = id, hablante = h, texto = t, siguiente = siguiente, evento = ev, opciones = opciones };
    static OpcionDialogo O(string t, string d, string ev = null) => new OpcionDialogo { texto = t, destino = d, evento = ev };

    // M05 · LA CAJA B
    public static ConversacionDialogo M05_CajaB() => Conv("inicio",
        N("inicio", "Amaia", "El pendrive descifra una sola palabra: 'Mendialde'. Una sociedad pantalla. La fábrica le factura aire y el dinero sale del valle por una carretera que conoces.", siguiente:"plan"),
        N("plan", "Manu", "(La N-1 de noche. Una furgoneta hace el mismo viaje cada martes, cargada de nada. Hoy es martes.)",
          opciones:new[]{ O("Seguirla sin que me vean (sigilo)","sigilo"), O("Cortarle el paso de frente","frente") }),
        N("sigilo", "Amaia", "Bien. Las fotos valen más que tú muerto en una cuneta. Discreto, Manu.", siguiente:"caja", ev:"subir_apoyo"),
        N("frente", "Manu", "(Dos hombres bajan. No preguntan quién eres: ya lo saben. Eso, en este valle, es lo más cerca que estás de una presentación formal.)", siguiente:"caja", ev:"bajar_apoyo"),
        N("caja", "Manu", "(Dentro: facturas, un sello, y un nombre que se repite. El del Francés. La caja B no estaba escondida. Estaba esperando a alguien lo bastante tonto para mirarla.)", ev:"pista_caja_b")
    );

    // M06 · MANIFA
    public static ConversacionDialogo M06_Manifa() => Conv("inicio",
        N("inicio", "Patxi", "Han venido más de los que esperaba. Y antidisturbios, también más. Tú decides cómo acaba esto, chaval. La gente te mira a ti.", siguiente:"decision"),
        N("decision", "Manu", "(Pancartas, megáfonos, y delante una hilera de escudos. Una piedra ahora y mañana somos los violentos en el periódico de Rekalde.)",
          opciones:new[]{ O("Aguantar. Sentados, sin un grito de más.","pacifica"), O("Que empujen ellos primero. Tensar.","desborde"), O("Subir a hablar yo al megáfono.","megafono") }),
        N("pacifica", "Amaia", "Han cargado y no han encontrado a quién pegar. Eso, en una foto, vale por mil. Hoy ha ganado el pueblo sin tirar una piedra.", ev:"manifa_pacifica"),
        N("desborde", "Amaia", "Ha ardido un contenedor y con él, parte de la razón que teníamos. Mañana hablarán del fuego, no de la caja B. Lo sabes.", ev:"manifa_desbordada"),
        N("megafono", "Manu", "(No preparas nada. Dices el nombre de Joseba, dices 'no nos vamos', y te callas. A veces el mejor discurso es el que no termina.)", ev:"manifa_pacifica")
    );

    // M07 · LA SOBRINA
    public static ConversacionDialogo M07_Sobrina() => Conv("inicio",
        N("inicio", "Nerea", "Yo solo llevé un sobre, Manu. Me dijeron que era de la fábrica, para un proveedor. No sabía... no sabía que era esto.", siguiente:"manu"),
        N("manu", "Manu", "(Diecinueve años y ya le han enseñado que en este valle nadie pregunta qué hay en el sobre. Joseba la quería fuera de todo esto.)",
          opciones:new[]{ O("No es culpa tuya. Te saco de en medio.","proteger"), O("Necesito que recuerdes a quién se lo diste.","usar") }),
        N("proteger", "Nerea", "...Gracias. Eres como él, ¿sabes? Cabezón e incapaz de mirar para otro lado. Ojalá te dure más que a él.", ev:"subir_apoyo"),
        N("usar", "Nerea", "Un hombre con gabardina cara y acento de fuera. El Francés. Te lo digo, pero, Manu... si me pasa algo, esto es culpa tuya tanto como suya.", ev:"pista_caja_b"),
        N("cierre", "Manu", "", null)
    );

    // M08 · SARA
    public static ConversacionDialogo M08_Sara() => Conv("inicio",
        N("inicio", "Sara", "No te he citado. Esta conversación no existe. Tengo media comisaría que mira a Rekalde antes de mirar la ley. Pero la otra media, no.", siguiente:"trato"),
        N("trato", "Sara", "Tú consigues que la caja B llegue a un juez que no le deba favores. Yo me aseguro de que no 'desaparezca' por el camino. Cada uno su mitad.",
          opciones:new[]{ O("Trato. Pero si me fallas, estoy solo.","acepta"), O("¿Por qué ahora? ¿Por qué tú?","porque"), O("No me fío de un uniforme.","rechaza") }),
        N("acepta", "Sara", "Si te fallo, será porque me han enterrado a mí primero. Es lo más parecido a una promesa que te puedo hacer.", ev:"aliada_sara"),
        N("porque", "Sara", "Porque entré en esto para meter a los malos, y llevo años poniendo multas de aparcamiento mientras el peor del valle inaugura rotondas. Por eso.", siguiente:"acepta"),
        N("rechaza", "Sara", "Lógico. Yo tampoco me fiaría. Pero soy la única puerta legal que te queda, y las otras puertas de este pueblo se cierran con gente dentro.", ev:"perder_aliado")
    );

    // M09 · EL INCENDIO
    public static ConversacionDialogo M09_Incendio() => Conv("inicio",
        N("inicio", "Manu", "(El bar de Patxi arde. No es un accidente: los accidentes no empiezan por tres sitios a la vez. Y Patxi no ha salido.)", siguiente:"amaia"),
        N("amaia", "Amaia", "Manu... lo siento. Sabía demasiado y hablaba con todos. Le quisieron callar y de paso mandar un recado. El recado eres tú.", ev:"muere_patxi", siguiente:"decision"),
        N("decision", "Manu", "(Ya no hay humor que valga. Solo el olor a quemado y una cuenta que alguien va a pagar.)",
          opciones:new[]{ O("Esto se acaba. Voy a por el Francés.","ira"), O("Lo hacemos legal. Por Patxi, bien hecho.","ley") }),
        N("ira", "Amaia", "Lo entiendo. Pero si entras en su terreno con sus reglas, ganas tú y pierde todo lo demás. Aun así, no te vas solo.", ev:"bajar_apoyo"),
        N("ley", "Amaia", "Patxi habría preferido eso: que esto sirviera, no que añadiera otro entierro. Vamos a clavarlos con papeles, no con sangre.", ev:"subir_apoyo")
    );
}
