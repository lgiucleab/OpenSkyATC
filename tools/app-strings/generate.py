"""Writes the app's UI strings (English and Spanish) as .resx files from one table, so both languages always have
the same keys. Run: python tools/app-strings/generate.py  (outputs src/OpenSkyAtc.App/Resources/Strings*.resx)."""
from pathlib import Path
from xml.sax.saxutils import escape

OUT = Path(__file__).resolve().parents[2] / "src" / "OpenSkyAtc.App" / "Resources"

STRINGS = {
    # Main window
    "App.Title": ("OpenSkyATC", "OpenSkyATC"),
    "Main.StartFlight": ("Start flight", "Iniciar vuelo"),
    "Main.EndFlight": ("End flight", "Terminar vuelo"),
    "Main.ImportPln": ("Open .pln", "Abrir .pln"),
    "Main.ImportPlan": ("Import plan", "Importar plan"),
    "Main.Settings": ("Settings", "Ajustes"),
    "Main.Compact": ("Compact", "Compacto"),
    "Main.Expand": ("Expand", "Ampliar"),
    "Main.HardMode": ("Hard mode (type readbacks yourself)", "Modo difícil (escribe tú las colaciones)"),
    "Main.TaxiArrows": ("Taxi arrows", "Flechas de rodaje"),
    "Main.You": ("You", "Tú"),
    "Main.Callsign": ("Callsign", "Indicativo"),
    "Main.Route": ("Route", "Ruta"),
    "Main.Phase": ("Phase", "Fase"),
    "Main.Cleared": ("Cleared", "Autorizado"),
    "Main.TalkingTo": ("Talking to", "Hablando con"),
    "Main.Next": ("Next: contact {0} {1}", "Siguiente: contacta con {0} {1}"),
    "Main.Tune": ("Tune {0} {1} (you are on {2})", "Sintoniza {0} {1} (estás en {2})"),
    "Main.NoFlight": ("No flight yet. Set your plan source in Settings, then start a flight.", "Aún no hay vuelo. Elige la fuente del plan en Ajustes y empieza un vuelo."),
    "Main.Arrows": ("Taxi arrows: {0} shown · {1} m ahead", "Flechas de rodaje: {0} visibles · {1} m por delante"),
    "Main.Squawk": ("sq {0}", "sq {0}"),
    "Main.Paused": ("Sim paused", "Simulador en pausa"),
    "Main.NothingToSay": ("Nothing to say right now", "Nada que decir ahora"),
    "Main.PlnFilter": ("MSFS flight plans (*.pln)|*.pln", "Planes de vuelo de MSFS (*.pln)|*.pln"),
    # Flight phases
    "Phase.Preflight": ("Preflight", "Prevuelo"),
    "Phase.Cleared": ("Cleared", "Autorizado"),
    "Phase.Pushback": ("Pushback", "Remolque"),
    "Phase.TaxiOut": ("Taxi out", "Rodaje de salida"),
    "Phase.Holding": ("Holding point", "Punto de espera"),
    "Phase.TakeoffRoll": ("Takeoff roll", "Carrera de despegue"),
    "Phase.InitialClimb": ("Initial climb", "Ascenso inicial"),
    "Phase.Departure": ("Departure", "Salida"),
    "Phase.EnRoute": ("En route", "En ruta"),
    "Phase.Descent": ("Descent", "Descenso"),
    "Phase.Approach": ("Approach", "Aproximación"),
    "Phase.Final": ("Final", "Final"),
    "Phase.LandingRoll": ("Landing roll", "Carrera de aterrizaje"),
    "Phase.TaxiIn": ("Taxi in", "Rodaje de llegada"),
    "Phase.Parked": ("Parked", "Estacionado"),
    # Status
    "Status.Idle": ("Not started", "Sin iniciar"),
    "Status.ImportingPlan": ("Importing plan", "Importando plan"),
    "Status.WaitingForSim": ("Waiting for MSFS", "Esperando a MSFS"),
    "Status.LoadingAirports": ("Loading airports", "Cargando aeropuertos"),
    "Status.SearchingCenters": ("Finding control centres", "Buscando centros de control"),
    "Status.DownloadingVoices": ("Downloading voices", "Descargando voces"),
    "Status.LoadingSpeech": ("Loading speech recognition", "Cargando reconocimiento de voz"),
    "Status.Running": ("MSFS connected", "MSFS conectado"),
    "Status.SimDisconnected": ("MSFS disconnected", "MSFS desconectado"),
    "Status.Failed": ("Could not start", "No se pudo iniciar"),
    "Status.Stopped": ("Flight ended", "Vuelo terminado"),
    "Status.Ptt": ("Transmitting", "Transmitiendo"),
    "Status.PttReady": ("PTT ready", "PTT listo"),
    "Status.Listening": ("Listening", "Escuchando"),
    "Status.Transcribing": ("Understanding", "Interpretando"),
    "Panel.Arrows": ("Taxi arrows", "Flechas de rodaje"),
    "Panel.ReadbackByVoice": ("Read back by voice: hold push-to-talk", "Colaciona por voz: mantén pulsar para hablar"),
    # Pilot calls (buttons)
    "Intent.Readback": ("Read back", "Colacionar"),
    "Intent.ReadbackForm": ("Read back...", "Colacionar..."),
    "Intent.RequestClearance": ("Request clearance", "Pedir autorización"),
    "Intent.RequestPushStart": ("Request push and start", "Pedir remolque y arranque"),
    "Intent.RequestTaxi": ("Request taxi", "Pedir rodaje"),
    "Intent.ReadyForDeparture": ("Ready for departure", "Listo para salir"),
    "Intent.RejectTakeoff": ("Reject takeoff", "Abortar despegue"),
    "Intent.CheckIn": ("Check in", "Notificar contacto"),
    "Intent.RequestLevel": ("Request level", "Pedir nivel"),
    "Intent.RequestDirect": ("Request direct", "Pedir directo"),
    "Intent.RequestDescent": ("Request descent", "Pedir descenso"),
    "Intent.ReportEstablished": ("Report established", "Notificar establecido"),
    "Intent.GoingAround": ("Going around", "Motor y al aire"),
    "Intent.ReportVacated": ("Report runway vacated", "Notificar pista libre"),
    "Intent.SayAgain": ("Say again", "Repita"),
    "Intent.Wilco": ("Wilco", "Wilco"),
    "Intent.Unable": ("Unable", "Imposible"),
    "Intent.Mayday": ("Mayday", "Mayday"),
    "Intent.PanPan": ("Pan-pan", "Pan-pan"),
    # Readback form
    "Readback.Title": ("Read back", "Colacionar"),
    "Readback.Intro": ("Type what ATC told you. Values are checked like a real readback.", "Escribe lo que te dijo el ATC. Se comprueba como una colación real."),
    "Readback.Send": ("Send readback", "Enviar colación"),
    "Readback.Cancel": ("Cancel", "Cancelar"),
    "Item.Destination": ("Cleared to", "Autorizado a"),
    "Item.Route": ("Route or SID", "Ruta o SID"),
    "Item.InitialClimb": ("Initial climb", "Ascenso inicial"),
    "Item.Squawk": ("Squawk", "Squawk"),
    "Item.HoldingPoint": ("Holding point", "Punto de espera"),
    "Item.Runway": ("Runway", "Pista"),
    "Item.HoldShorts": ("Hold short of", "Mantener fuera de"),
    "Item.Clearance": ("Clearance", "Autorización"),
    "Item.Altitude": ("Altitude", "Altitud"),
    "Item.Heading": ("Heading", "Rumbo"),
    "Item.Speed": ("Speed (kt)", "Velocidad (kt)"),
    "Item.DirectTo": ("Direct to", "Directo a"),
    "Item.Frequency": ("Frequency", "Frecuencia"),
    "Item.Qnh": ("QNH", "QNH"),
    "Item.Stand": ("Stand", "Puesto"),
    "Item.Facing": ("Facing", "Orientado al"),
    # Settings
    "Settings.Title": ("Settings", "Ajustes"),
    "Settings.Save": ("Save", "Guardar"),
    "Settings.Cancel": ("Cancel", "Cancelar"),
    "Settings.FlightPlan": ("Flight plan", "Plan de vuelo"),
    "Settings.PlanSource": ("Plan source", "Fuente del plan"),
    "Settings.SourceSimBrief": ("SimBrief (latest plan)", "SimBrief (último plan)"),
    "Settings.SourcePln": ("MSFS .pln file", "Archivo .pln de MSFS"),
    "Settings.SimBriefId": ("SimBrief Pilot ID", "ID de piloto de SimBrief"),
    "Settings.SimBriefHelp": ("On the SimBrief Account Settings page. For flight simulation only.", "En la página de ajustes de cuenta de SimBrief. Solo para simulación."),
    "Settings.PlnFile": ("Flight plan file", "Archivo del plan"),
    "Settings.Browse": ("Browse", "Examinar"),
    "Settings.Voice": ("Voice", "Voz"),
    "Settings.VoiceEnabled": ("Speak ATC calls", "Leer en voz alta las llamadas del ATC"),
    "Settings.Volume": ("Volume", "Volumen"),
    "Settings.RadioFilter": ("Radio effect", "Efecto de radio"),
    "Settings.Voices": ("Controller voices", "Voces de controlador"),
    "Settings.TestVoice": ("Test", "Probar"),
    "Settings.PushToTalk": ("Push-to-talk", "Pulsar para hablar"),
    "Settings.PttInput": ("Key or button in the sim (SimConnect)", "Tecla o botón en el simulador (SimConnect)"),
    "Settings.PttHelp": ("Examples: VK_LCONTROL+T, joystick:0:button:3 (yoke and joystick buttons work here). Works while the sim has focus.", "Ejemplos: VK_LCONTROL+T, joystick:0:button:3 (aquí funcionan los botones del yugo y del joystick). Funciona con el simulador en primer plano."),
    "Settings.PttKeyboard": ("Keyboard key in any window", "Tecla del teclado en cualquier ventana"),
    "Settings.PttKeyboardHelp": ("Examples: F12, Ctrl+Space, RCtrl, NumPad0. The key still reaches the sim, so pick one it does not use.", "Ejemplos: F12, Ctrl+Space, RCtrl, NumPad0. La tecla también llega al simulador: elige una que no use."),
    "Settings.AtcKey": ("Use the sim's ATC key (Scroll Lock) as push-to-talk", "Usar la tecla ATC del simulador (Bloq Despl) como pulsar para hablar"),
    "Settings.Speech": ("Speech recognition", "Reconocimiento de voz"),
    "Settings.WhisperModel": ("Whisper model", "Modelo de Whisper"),
    "Settings.VoiceInput": ("Talk to ATC with your voice (hold push-to-talk)", "Hablar con el ATC con tu voz (mantén pulsar para hablar)"),
    "Settings.Microphone": ("Microphone", "Micrófono"),
    "Settings.DefaultMicrophone": ("Windows default", "Predeterminado de Windows"),
    "Settings.SpeechHelp": ("Whisper runs on your PC; nothing is sent anywhere. The model downloads once on first use. Larger models understand more but answer later. If ATC is not sure what you said, it asks you to say again.", "Whisper funciona en tu PC; no se envía nada. El modelo se descarga una vez al usarlo por primera vez. Los modelos grandes entienden más pero responden más tarde. Si el ATC no está seguro de lo que dijiste, te pide que lo repitas."),
    "Settings.Arrows": ("Taxi arrows", "Flechas de rodaje"),
    "Settings.ArrowsEnabled": ("Show taxi arrows in the sim", "Mostrar flechas de rodaje en el simulador"),
    "Settings.LookAhead": ("Look-ahead (m)", "Distancia por delante (m)"),
    "Settings.Spacing": ("Spacing (m)", "Separación (m)"),
    "Settings.Language": ("Language", "Idioma"),
    "Settings.LanguageHelp": ("The app's language. ATC always speaks English.", "Idioma de la aplicación. El ATC siempre habla inglés."),
    "Settings.AppliesNext": ("Voices, push-to-talk and the plan source apply to the next flight.", "Las voces, el pulsar para hablar y la fuente del plan se aplican al siguiente vuelo."),
    "Settings.Credits": ("Voices: CSTR VCTK Corpus and LibriTTS-R (CC BY 4.0), Joe (CC0), Norman, John and Cori (public domain); Piper and sherpa-onnx.", "Voces: CSTR VCTK Corpus y LibriTTS-R (CC BY 4.0), Joe (CC0), Norman, John y Cori (dominio público); Piper y sherpa-onnx."),
    # Errors
    "Error.Start": ("Could not start the flight", "No se pudo iniciar el vuelo"),
    "Error.NeedSimBriefId": ("Enter your SimBrief Pilot ID in Settings first.", "Primero introduce tu ID de piloto de SimBrief en Ajustes."),
    "Error.NeedPln": ("Choose a .pln file first (Open .pln).", "Primero elige un archivo .pln (Abrir .pln)."),
}


def resx(lang_index: int) -> str:
    rows = "\n".join(
        f'  <data name="{escape(key)}" xml:space="preserve">\n    <value>{escape(values[lang_index])}</value>\n  </data>'
        for key, values in STRINGS.items())
    return f'''<?xml version="1.0" encoding="utf-8"?>
<!-- Generated by tools/app-strings/generate.py; edit the table there. -->
<root>
  <resheader name="resmimetype"><value>text/microsoft-resx</value></resheader>
  <resheader name="version"><value>2.0</value></resheader>
  <resheader name="reader"><value>System.Resources.ResXResourceReader, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value></resheader>
  <resheader name="writer"><value>System.Resources.ResXResourceWriter, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value></resheader>
{rows}
</root>
'''


if __name__ == "__main__":
    OUT.mkdir(parents=True, exist_ok=True)
    (OUT / "Strings.resx").write_text(resx(0), encoding="utf-8")
    (OUT / "Strings.es.resx").write_text(resx(1), encoding="utf-8")
    print(f"Wrote {len(STRINGS)} strings in English and Spanish to {OUT}")
