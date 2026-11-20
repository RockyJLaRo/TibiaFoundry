"""New and changed texts of the 2026 website redesign, in every language (merged into <lang>.json by merge_texts()).

    python -I website/src/locales/redesign_texts.py      (writes the five locale files; run once, then edit the JSON files)

Kept as a record of the redesign's copy; the JSON files remain the source the build reads. Later edits (e.g. the
Tibia tools page title, js.allTools, js.hubTitle, the _meta.sameAsEnglish entries and the English Title Case of 2026-11-19)
were made in the JSON files directly, so do not run this again. Entries of keys the site no longer has were removed.
"""
import json
import os

REMOVE = ["band", "hero.github"]

T = {
"nav.download": {"en": "Download", "pt-BR": "Baixar", "pl": "Pobierz", "es": "Descargar", "de": "Download"},
"nav.legal": {"en": "Legal & transparency", "pt-BR": "Aspectos legais e transparência", "pl": "Informacje prawne", "es": "Aviso legal y transparencia", "de": "Rechtliches & Transparenz"},
"nav.breadcrumb": {"en": "Breadcrumb", "pt-BR": "Você está aqui", "pl": "Ścieżka nawigacji", "es": "Ruta de navegación", "de": "Brotkrümelnavigation"},
"nav.tibia": {"en": "Tibia tools", "pt-BR": "Ferramentas Tibia", "pl": "Narzędzia Tibii", "es": "Herramientas de Tibia", "de": "Tibia-Tools"},

"home.hero.title": {
  "en": "Tibia tools, live game data and a desktop toolkit for fansite creators",
  "pt-BR": "Ferramentas de Tibia, dados ao vivo do jogo e um kit para criadores de fansites",
  "pl": "Narzędzia do Tibii, aktualne dane z gry i program dla twórców fansite'ów",
  "es": "Herramientas de Tibia, datos del juego en vivo y un kit para creadores de fansites",
  "de": "Tibia-Tools, aktuelle Spieldaten und ein Desktop-Werkzeugkasten für Fansite-Macher"},
"home.hero.lead": {
  "en": "Look up characters, worlds, highscores, houses and bosses with live data – free, right in your browser. Building a fansite or editing TibiaWiki? The free Windows app turns your own Tibia client into wiki-ready images and data.",
  "pt-BR": "Consulte personagens, mundos, highscores, casas e bosses com dados ao vivo – grátis, direto no navegador. Tem um fansite ou edita a TibiaWiki? O programa gratuito para Windows transforma o seu próprio cliente do Tibia em imagens e dados prontos para a wiki.",
  "pl": "Sprawdzaj postacie, światy, highscores, domy i bossów na aktualnych danych – za darmo, w przeglądarce. Prowadzisz fansite albo edytujesz TibiaWiki? Darmowy program na Windows zamienia Twojego klienta Tibii w gotowe do wiki grafiki i dane.",
  "es": "Consulta personajes, mundos, highscores, casas y jefes con datos en vivo, gratis y en tu navegador. ¿Tienes un fansite o editas TibiaWiki? El programa gratuito para Windows convierte tu propio cliente de Tibia en imágenes y datos listos para la wiki.",
  "de": "Charaktere, Welten, Highscores, Häuser und Bosse mit aktuellen Daten nachschlagen – kostenlos, direkt im Browser. Du baust eine Fansite oder schreibst im TibiaWiki? Das kostenlose Windows-Programm macht aus deinem eigenen Tibia-Client wikifertige Bilder und Daten."},
"home.today.title": {"en": "Today in Tibia", "pt-BR": "Hoje no Tibia", "pl": "Dziś w Tibii", "es": "Hoy en Tibia", "de": "Heute in Tibia"},
"home.tools.all": {"en": "All Tibia tools", "pt-BR": "Todas as ferramentas", "pl": "Wszystkie narzędzia", "es": "Todas las herramientas", "de": "Alle Tibia-Tools"},
"home.app.fact.version": {"en": "Version {version}", "pt-BR": "Versão {version}", "pl": "Wersja {version}", "es": "Versión {version}", "de": "Version {version}"},

"notice.title": {"en": "Important download notice", "pt-BR": "Aviso importante sobre o download", "pl": "Ważna informacja o pobieraniu", "es": "Aviso importante sobre la descarga", "de": "Wichtiger Hinweis zum Download"},
"notice.text1": {
  "en": "Tibia Foundry is independently developed software created by Rocky LaRochelle. It is not created, developed, distributed, or supported by CipSoft GmbH.",
  "pt-BR": "O Tibia Foundry é um software desenvolvido de forma independente por Rocky LaRochelle. Ele não é criado, desenvolvido, distribuído nem suportado pela CipSoft GmbH.",
  "pl": "Tibia Foundry to niezależnie rozwijane oprogramowanie stworzone przez Rocky'ego LaRochelle. Nie jest tworzone, rozwijane, rozpowszechniane ani wspierane przez CipSoft GmbH.",
  "es": "Tibia Foundry es un software desarrollado de forma independiente por Rocky LaRochelle. No ha sido creado, desarrollado, distribuido ni recibe soporte de CipSoft GmbH.",
  "de": "Tibia Foundry ist unabhängig entwickelte Software von Rocky LaRochelle. Sie wird nicht von der CipSoft GmbH erstellt, entwickelt, vertrieben oder unterstützt."},
"notice.text2": {
  "en": "Tibia Foundry is an independent fan-made project and is not an official Tibia product.",
  "pt-BR": "O Tibia Foundry é um projeto independente feito por fã e não é um produto oficial do Tibia.",
  "pl": "Tibia Foundry to niezależny projekt fanowski, a nie oficjalny produkt Tibii.",
  "es": "Tibia Foundry es un proyecto independiente hecho por un fan y no es un producto oficial de Tibia.",
  "de": "Tibia Foundry ist ein unabhängiges Fanprojekt und kein offizielles Tibia-Produkt."},
"notice.text3": {
  "en": "CipSoft GmbH is not responsible for this software. Please download it only from the official Tibia Foundry distribution sources and review the published release information before installing.",
  "pt-BR": "A CipSoft GmbH não é responsável por este software. Baixe-o somente das fontes oficiais de distribuição do Tibia Foundry e confira as informações publicadas da versão antes de instalar.",
  "pl": "CipSoft GmbH nie odpowiada za to oprogramowanie. Pobieraj je wyłącznie z oficjalnych źródeł Tibia Foundry i przed instalacją przeczytaj opublikowane informacje o wydaniu.",
  "es": "CipSoft GmbH no es responsable de este software. Descárgalo solo desde las fuentes oficiales de distribución de Tibia Foundry y revisa la información publicada de la versión antes de instalarlo.",
  "de": "Die CipSoft GmbH ist für diese Software nicht verantwortlich. Lade sie nur aus den offiziellen Quellen von Tibia Foundry herunter und lies vor der Installation die veröffentlichten Versionsinformationen."},
"notice.more": {"en": "Download safety and release information", "pt-BR": "Segurança do download e informações da versão", "pl": "Bezpieczeństwo pobierania i informacje o wydaniu", "es": "Seguridad de la descarga e información de la versión", "de": "Download-Sicherheit und Versionsinformationen"},

"download.facts.title": {"en": "Release information", "pt-BR": "Informações da versão", "pl": "Informacje o wydaniu", "es": "Información de la versión", "de": "Versionsinformationen"},
"download.facts.version": {"en": "Version", "pt-BR": "Versão", "pl": "Wersja", "es": "Versión", "de": "Version"},
"download.facts.date": {"en": "Release date", "pt-BR": "Data de lançamento", "pl": "Data wydania", "es": "Fecha de publicación", "de": "Veröffentlicht am"},
"download.facts.dateUnknown": {"en": "Not recorded", "pt-BR": "Não registrada", "pl": "Nie podano", "es": "No registrada", "de": "Nicht angegeben"},
"download.facts.os": {"en": "Supported systems", "pt-BR": "Sistemas compatíveis", "pl": "Obsługiwane systemy", "es": "Sistemas compatibles", "de": "Unterstützte Systeme"},
"download.facts.packages": {"en": "Packages", "pt-BR": "Pacotes", "pl": "Pakiety", "es": "Paquetes", "de": "Pakete"},
"download.facts.packagesValue": {
  "en": "Windows Installer (MSI inside a ZIP), portable ZIP, source code ZIP",
  "pt-BR": "Windows Installer (MSI dentro de um ZIP), ZIP portátil, ZIP do código-fonte",
  "pl": "Instalator Windows (MSI w archiwum ZIP), wersja przenośna ZIP, kod źródłowy ZIP",
  "es": "Instalador de Windows (MSI dentro de un ZIP), ZIP portable, ZIP del código fuente",
  "de": "Windows Installer (MSI in einem ZIP), portables ZIP, Quellcode-ZIP"},
"download.facts.source": {"en": "Source code", "pt-BR": "Código-fonte", "pl": "Kod źródłowy", "es": "Código fuente", "de": "Quellcode"},
"download.facts.notes": {"en": "Release notes", "pt-BR": "Notas da versão", "pl": "Informacje o wydaniu", "es": "Notas de la versión", "de": "Versionshinweise"},
"download.facts.checksums": {"en": "Checksums", "pt-BR": "Somas de verificação", "pl": "Sumy kontrolne", "es": "Sumas de verificación", "de": "Prüfsummen"},
"download.facts.signing": {"en": "Code signing", "pt-BR": "Assinatura de código", "pl": "Podpis cyfrowy", "es": "Firma de código", "de": "Codesignatur"},
"download.facts.signedNo": {
  "en": "Not code-signed. Windows SmartScreen may warn about a new download; compare the SHA-256 checksum instead.",
  "pt-BR": "Sem assinatura de código. O Windows SmartScreen pode alertar sobre um download novo; compare a soma SHA-256.",
  "pl": "Brak podpisu cyfrowego. Windows SmartScreen może ostrzec przed nowym plikiem; porównaj sumę SHA-256.",
  "es": "Sin firma de código. Windows SmartScreen puede advertir sobre una descarga nueva; compara la suma SHA-256.",
  "de": "Nicht signiert. Windows SmartScreen kann bei einem neuen Download warnen; vergleiche stattdessen die SHA-256-Prüfsumme."},
"download.facts.signedYes": {"en": "Signed", "pt-BR": "Assinado", "pl": "Podpisany", "es": "Firmado", "de": "Signiert"},
"download.facts.scan": {"en": "Malware scan", "pt-BR": "Verificação de malware", "pl": "Skanowanie antywirusowe", "es": "Análisis de malware", "de": "Malware-Prüfung"},
"download.facts.scanValue": {
  "en": "No third-party malware scan is published for this release. Scan the file with your own antivirus before installing.",
  "pt-BR": "Nenhuma verificação de malware de terceiros foi publicada para esta versão. Verifique o arquivo com o seu antivírus antes de instalar.",
  "pl": "Dla tego wydania nie opublikowano skanu zewnętrznego. Przed instalacją sprawdź plik własnym programem antywirusowym.",
  "es": "No se ha publicado un análisis de malware de terceros para esta versión. Analiza el archivo con tu antivirus antes de instalarlo.",
  "de": "Für diese Version ist keine Malware-Prüfung durch Dritte veröffentlicht. Prüfe die Datei vor der Installation mit deinem eigenen Virenscanner."},
"download.safety.title": {"en": "Download safely", "pt-BR": "Baixe com segurança", "pl": "Pobieraj bezpiecznie", "es": "Descarga con seguridad", "de": "Sicher herunterladen"},
"download.safety.official": {
  "en": "Download only from this page or from the GitHub releases of RockyJLaRo/TibiaFoundry – every link goes over HTTPS straight to GitHub.",
  "pt-BR": "Baixe somente desta página ou das releases do GitHub de RockyJLaRo/TibiaFoundry – todos os links vão por HTTPS direto ao GitHub.",
  "pl": "Pobieraj tylko z tej strony albo z wydań RockyJLaRo/TibiaFoundry na GitHubie – każdy link prowadzi przez HTTPS prosto do GitHuba.",
  "es": "Descarga solo desde esta página o desde las releases de RockyJLaRo/TibiaFoundry en GitHub: todos los enlaces van por HTTPS directamente a GitHub.",
  "de": "Lade nur über diese Seite oder die GitHub-Releases von RockyJLaRo/TibiaFoundry herunter – jeder Link führt per HTTPS direkt zu GitHub."},
"download.safety.verify": {
  "en": "Compare the file's SHA-256 checksum with the value shown here and in SHA256SUMS.txt.",
  "pt-BR": "Compare a soma SHA-256 do arquivo com o valor mostrado aqui e no SHA256SUMS.txt.",
  "pl": "Porównaj sumę SHA-256 pliku z wartością podaną tutaj i w SHA256SUMS.txt.",
  "es": "Compara la suma SHA-256 del archivo con el valor que aparece aquí y en SHA256SUMS.txt.",
  "de": "Vergleiche die SHA-256-Prüfsumme der Datei mit dem Wert hier und in SHA256SUMS.txt."},
"download.safety.nothingBundled": {
  "en": "The packages contain Tibia Foundry and the open-source components listed in THIRD-PARTY-NOTICES.txt – no bundled offers, adware or other programs.",
  "pt-BR": "Os pacotes contêm o Tibia Foundry e os componentes de código aberto listados em THIRD-PARTY-NOTICES.txt – sem ofertas embutidas, adware ou outros programas.",
  "pl": "Pakiety zawierają Tibia Foundry i komponenty open source wymienione w THIRD-PARTY-NOTICES.txt – bez dołączonych ofert, adware ani innych programów.",
  "es": "Los paquetes contienen Tibia Foundry y los componentes de código abierto que se enumeran en THIRD-PARTY-NOTICES.txt, sin ofertas añadidas, adware ni otros programas.",
  "de": "Die Pakete enthalten Tibia Foundry und die in THIRD-PARTY-NOTICES.txt genannten Open-Source-Komponenten – keine beigepackten Angebote, Adware oder andere Programme."},
"download.safety.purpose": {
  "en": "Tibia Foundry makes images and data for TibiaWiki and fansites from your own client. It is not a bot, cheat or automation tool, and it does not interact with the game while you play.",
  "pt-BR": "O Tibia Foundry cria imagens e dados para a TibiaWiki e fansites a partir do seu próprio cliente. Não é bot, cheat nem ferramenta de automação, e não interage com o jogo enquanto você joga.",
  "pl": "Tibia Foundry tworzy grafiki i dane dla TibiaWiki i fansite'ów z Twojego klienta. Nie jest botem, cheatem ani narzędziem do automatyzacji i nie ingeruje w grę podczas rozgrywki.",
  "es": "Tibia Foundry crea imágenes y datos para TibiaWiki y fansites a partir de tu propio cliente. No es un bot, un cheat ni una herramienta de automatización, y no interactúa con el juego mientras juegas.",
  "de": "Tibia Foundry erstellt aus deinem eigenen Client Bilder und Daten für das TibiaWiki und Fansites. Es ist kein Bot, Cheat oder Automatisierungstool und greift nicht ins laufende Spiel ein."},
"download.reqTitle": {"en": "System requirements", "pt-BR": "Requisitos do sistema", "pl": "Wymagania systemowe", "es": "Requisitos del sistema", "de": "Systemanforderungen"},

"footer.about": {
  "en": "Free Tibia tools in your browser and a desktop toolkit for TibiaWiki editors and fansite creators – an independent fan project.",
  "pt-BR": "Ferramentas de Tibia grátis no navegador e um kit para PC para editores da TibiaWiki e criadores de fansites – um projeto independente de fã.",
  "pl": "Darmowe narzędzia do Tibii w przeglądarce i program dla edytorów TibiaWiki oraz twórców fansite'ów – niezależny projekt fanowski.",
  "es": "Herramientas de Tibia gratis en tu navegador y un kit de escritorio para editores de TibiaWiki y creadores de fansites: un proyecto independiente de fans.",
  "de": "Kostenlose Tibia-Tools im Browser und ein Desktop-Werkzeugkasten für TibiaWiki-Autoren und Fansite-Macher – ein unabhängiges Fanprojekt."},
"footer.legal": {
  "en": "Tibia is made and operated by CipSoft GmbH. Tibia, its website, game graphics and data are copyrighted by CipSoft GmbH, and content provided by CipSoft remains its property. Tibia Foundry is an independent fan project and is not affiliated with or endorsed by CipSoft GmbH or Fandom.",
  "pt-BR": "O Tibia é criado e operado pela CipSoft GmbH. O Tibia, seu site, gráficos e dados do jogo são protegidos por direitos autorais da CipSoft GmbH, e o conteúdo fornecido pela CipSoft continua sendo dela. O Tibia Foundry é um projeto independente de fã, sem afiliação ou endosso da CipSoft GmbH ou da Fandom.",
  "pl": "Tibię tworzy i prowadzi CipSoft GmbH. Tibia, jej strona, grafiki i dane z gry są chronione prawem autorskim CipSoft GmbH, a materiały udostępnione przez CipSoft pozostają jej własnością. Tibia Foundry to niezależny projekt fanowski, niepowiązany z CipSoft GmbH ani Fandom i przez nie niepopierany.",
  "es": "Tibia es creado y operado por CipSoft GmbH. Tibia, su sitio web, los gráficos y los datos del juego tienen copyright de CipSoft GmbH, y el contenido proporcionado por CipSoft sigue siendo de su propiedad. Tibia Foundry es un proyecto independiente de fans y no está afiliado ni respaldado por CipSoft GmbH ni por Fandom.",
  "de": "Tibia wird von der CipSoft GmbH entwickelt und betrieben. Tibia, die Website, Spielgrafiken und Spieldaten sind urheberrechtlich geschützt (© CipSoft GmbH); von CipSoft bereitgestellte Inhalte bleiben deren Eigentum. Tibia Foundry ist ein unabhängiges Fanprojekt und weder mit der CipSoft GmbH noch mit Fandom verbunden oder von ihnen unterstützt."},

"pages.legal.title": {"en": "Legal & transparency", "pt-BR": "Aspectos legais e transparência", "pl": "Informacje prawne i przejrzystość", "es": "Aviso legal y transparencia", "de": "Rechtliches & Transparenz"},
"pages.legal.description": {
  "en": "Copyright and CipSoft attribution, the responsible administrator, download safety, money and advertising, privacy and statistics, community rules and content sources of Tibia Foundry.",
  "pt-BR": "Direitos autorais e atribuição à CipSoft, administrador responsável, segurança dos downloads, dinheiro e publicidade, privacidade e estatísticas, regras da comunidade e fontes do conteúdo do Tibia Foundry.",
  "pl": "Prawa autorskie i informacja o CipSoft, odpowiedzialny administrator, bezpieczeństwo pobierania, pieniądze i reklamy, prywatność i statystyki, zasady społeczności oraz źródła treści Tibia Foundry.",
  "es": "Copyright y atribución a CipSoft, administrador responsable, seguridad de las descargas, dinero y publicidad, privacidad y estadísticas, normas de la comunidad y fuentes del contenido de Tibia Foundry.",
  "de": "Urheberrecht und CipSoft-Hinweis, verantwortlicher Administrator, Download-Sicherheit, Geld und Werbung, Datenschutz und Statistik, Community-Regeln und Inhaltsquellen von Tibia Foundry."},

"legal.title": {"en": "Legal & transparency", "pt-BR": "Aspectos legais e transparência", "pl": "Informacje prawne i przejrzystość", "es": "Aviso legal y transparencia", "de": "Rechtliches & Transparenz"},
"legal.lead": {
  "en": "Who runs Tibia Foundry, what belongs to whom, how downloads are published, what this site collects – and the rules it follows.",
  "pt-BR": "Quem mantém o Tibia Foundry, o que pertence a quem, como os downloads são publicados, o que este site coleta – e as regras que ele segue.",
  "pl": "Kto prowadzi Tibia Foundry, co do kogo należy, jak publikowane są pliki, co zbiera ta strona – i jakich zasad przestrzega.",
  "es": "Quién mantiene Tibia Foundry, qué pertenece a quién, cómo se publican las descargas, qué recopila este sitio y qué normas sigue.",
  "de": "Wer Tibia Foundry betreibt, was wem gehört, wie Downloads veröffentlicht werden, was diese Seite erfasst – und an welche Regeln sie sich hält."},
"legal.sections": {"en": "Sections of this page", "pt-BR": "Seções desta página", "pl": "Sekcje tej strony", "es": "Secciones de esta página", "de": "Abschnitte dieser Seite"},
"legal.rulesLink": {"en": "Tibia Rules", "pt-BR": "Regras do Tibia", "pl": "Zasady Tibii", "es": "Reglas de Tibia", "de": "Tibia-Regeln"},
"legal.copyright.title": {"en": "Copyright and CipSoft", "pt-BR": "Direitos autorais e CipSoft", "pl": "Prawa autorskie i CipSoft", "es": "Copyright y CipSoft", "de": "Urheberrecht und CipSoft"},
"legal.copyright.text1": {
  "en": "Tibia is a game created and operated by CipSoft GmbH. Tibia, the official Tibia website and their content are protected by copyright.",
  "pt-BR": "O Tibia é um jogo criado e operado pela CipSoft GmbH. O Tibia, o site oficial do Tibia e seu conteúdo são protegidos por direitos autorais.",
  "pl": "Tibia to gra stworzona i prowadzona przez CipSoft GmbH. Tibia, jej oficjalna strona i ich zawartość są chronione prawem autorskim.",
  "es": "Tibia es un juego creado y operado por CipSoft GmbH. Tibia, el sitio web oficial de Tibia y su contenido están protegidos por derechos de autor.",
  "de": "Tibia ist ein Spiel, das von der CipSoft GmbH entwickelt und betrieben wird. Tibia, die offizielle Tibia-Website und ihre Inhalte sind urheberrechtlich geschützt."},
"legal.copyright.text2": {
  "en": "Content provided by CipSoft – including the game graphics, images and data shown on this site and in the screenshots of Tibia Foundry – remains the property of CipSoft GmbH. Tibia Foundry claims no ownership of it.",
  "pt-BR": "O conteúdo fornecido pela CipSoft – incluindo gráficos, imagens e dados do jogo mostrados neste site e nas capturas de tela do Tibia Foundry – continua sendo propriedade da CipSoft GmbH. O Tibia Foundry não reivindica nenhum direito sobre ele.",
  "pl": "Materiały udostępnione przez CipSoft – w tym grafiki, obrazy i dane z gry widoczne na tej stronie i na zrzutach ekranu Tibia Foundry – pozostają własnością CipSoft GmbH. Tibia Foundry nie rości sobie do nich żadnych praw.",
  "es": "El contenido proporcionado por CipSoft, incluidos los gráficos, imágenes y datos del juego que se muestran en este sitio y en las capturas de Tibia Foundry, sigue siendo propiedad de CipSoft GmbH. Tibia Foundry no reclama ningún derecho sobre él.",
  "de": "Von CipSoft bereitgestellte Inhalte – auch die Spielgrafiken, Bilder und Daten auf dieser Seite und in den Screenshots von Tibia Foundry – bleiben Eigentum der CipSoft GmbH. Tibia Foundry beansprucht daran keine Rechte."},
"legal.copyright.text3": {
  "en": "Tibia Foundry is an independently developed fan project by Rocky LaRochelle. It is not affiliated with, endorsed, sponsored or approved by CipSoft GmbH.",
  "pt-BR": "O Tibia Foundry é um projeto de fã desenvolvido de forma independente por Rocky LaRochelle. Ele não é afiliado, endossado, patrocinado nem aprovado pela CipSoft GmbH.",
  "pl": "Tibia Foundry to niezależnie rozwijany projekt fanowski Rocky'ego LaRochelle. Nie jest powiązany z CipSoft GmbH ani przez nią popierany, sponsorowany czy zatwierdzony.",
  "es": "Tibia Foundry es un proyecto de fans desarrollado de forma independiente por Rocky LaRochelle. No está afiliado, respaldado, patrocinado ni aprobado por CipSoft GmbH.",
  "de": "Tibia Foundry ist ein unabhängig entwickeltes Fanprojekt von Rocky LaRochelle. Es ist weder mit der CipSoft GmbH verbunden noch von ihr unterstützt, gesponsert oder genehmigt."},
"legal.status.title": {"en": "Fansite Programme status", "pt-BR": "Situação no Programa de Fansites", "pl": "Status w programie fansite'ów", "es": "Estado en el Programa de Fansites", "de": "Status im Fansite-Programm"},
"legal.status.text1": {
  "en": "Tibia Foundry is not a promoted or supported fansite of CipSoft's Fansite Programme and shows no programme badge. It is an independent fan project.",
  "pt-BR": "O Tibia Foundry não é um fansite promovido nem apoiado do Programa de Fansites da CipSoft e não exibe nenhum selo do programa. É um projeto independente de fã.",
  "pl": "Tibia Foundry nie jest promowanym ani wspieranym fansite'em programu CipSoft i nie wyświetla żadnej odznaki programu. To niezależny projekt fanowski.",
  "es": "Tibia Foundry no es un fansite promocionado ni apoyado del Programa de Fansites de CipSoft y no muestra ninguna insignia del programa. Es un proyecto independiente de fans.",
  "de": "Tibia Foundry ist keine Promoted oder Supported Fansite des Fansite-Programms von CipSoft und zeigt kein Programm-Abzeichen. Es ist ein unabhängiges Fanprojekt."},
"legal.status.text2": {
  "en": "If that ever changes, this page will say so. The programme and its requirements are described on Tibia.com:",
  "pt-BR": "Se isso mudar, esta página dirá. O programa e seus requisitos estão descritos no Tibia.com:",
  "pl": "Jeśli to się zmieni, ta strona o tym poinformuje. Program i jego wymagania opisano na Tibia.com:",
  "es": "Si eso cambia, esta página lo indicará. El programa y sus requisitos se describen en Tibia.com:",
  "de": "Sollte sich das ändern, steht es hier. Das Programm und seine Anforderungen beschreibt Tibia.com:"},
"legal.status.programme": {"en": "Tibia Fansite Programme", "pt-BR": "Programa de Fansites do Tibia", "pl": "Program fansite'ów Tibii", "es": "Programa de Fansites de Tibia", "de": "Tibia-Fansite-Programm"},
"legal.admin.title": {"en": "Responsible administrator", "pt-BR": "Administrador responsável", "pl": "Odpowiedzialny administrator", "es": "Administrador responsable", "de": "Verantwortlicher Administrator"},
"legal.admin.name": {"en": "Name", "pt-BR": "Nome", "pl": "Imię i nazwisko", "es": "Nombre", "de": "Name"},
"legal.admin.role": {"en": "Role", "pt-BR": "Função", "pl": "Rola", "es": "Función", "de": "Rolle"},
"legal.admin.place": {"en": "Location", "pt-BR": "Localização", "pl": "Lokalizacja", "es": "Ubicación", "de": "Ort"},
"legal.admin.character": {"en": "Tibia character", "pt-BR": "Personagem no Tibia", "pl": "Postać w Tibii", "es": "Personaje de Tibia", "de": "Tibia-Charakter"},
"legal.admin.email": {"en": "Email", "pt-BR": "E-mail", "pl": "E-mail", "es": "Correo", "de": "E-Mail"},
"legal.admin.note": {
  "en": "Rocky runs this website and the project alone. Questions, corrections and reports go to this address; there is no support team and no promised response time.",
  "pt-BR": "Rocky mantém este site e o projeto sozinho. Dúvidas, correções e denúncias vão para este endereço; não há equipe de suporte nem prazo de resposta garantido.",
  "pl": "Rocky prowadzi tę stronę i projekt sam. Pytania, poprawki i zgłoszenia wysyłaj na ten adres; nie ma zespołu wsparcia ani gwarantowanego czasu odpowiedzi.",
  "es": "Rocky mantiene este sitio y el proyecto él solo. Las preguntas, correcciones y denuncias van a esta dirección; no hay equipo de soporte ni tiempo de respuesta garantizado.",
  "de": "Rocky betreibt diese Website und das Projekt allein. Fragen, Korrekturen und Meldungen gehen an diese Adresse; es gibt kein Support-Team und keine zugesagte Antwortzeit."},
"legal.downloads.title": {"en": "Downloads and safety", "pt-BR": "Downloads e segurança", "pl": "Pobieranie i bezpieczeństwo", "es": "Descargas y seguridad", "de": "Downloads und Sicherheit"},
"legal.downloads.whereTitle": {"en": "Official download sources", "pt-BR": "Fontes oficiais de download", "pl": "Oficjalne źródła pobierania", "es": "Fuentes oficiales de descarga", "de": "Offizielle Download-Quellen"},
"legal.downloads.where": {
  "en": "Tibia Foundry is published only on the Downloads page of tibiafoundry.com and in the releases of github.com/RockyJLaRo/TibiaFoundry. Copies from anywhere else are not official.",
  "pt-BR": "O Tibia Foundry é publicado apenas na página Downloads de tibiafoundry.com e nas releases de github.com/RockyJLaRo/TibiaFoundry. Cópias de qualquer outro lugar não são oficiais.",
  "pl": "Tibia Foundry jest publikowane wyłącznie na stronie pobierania tibiafoundry.com i w wydaniach github.com/RockyJLaRo/TibiaFoundry. Kopie z innych miejsc nie są oficjalne.",
  "es": "Tibia Foundry se publica solo en la página de descargas de tibiafoundry.com y en las releases de github.com/RockyJLaRo/TibiaFoundry. Las copias de cualquier otro lugar no son oficiales.",
  "de": "Tibia Foundry wird nur auf der Download-Seite von tibiafoundry.com und in den Releases von github.com/RockyJLaRo/TibiaFoundry veröffentlicht. Kopien von anderswo sind nicht offiziell."},
"legal.downloads.checkTitle": {"en": "How each release is checked", "pt-BR": "Como cada versão é verificada", "pl": "Jak sprawdzane jest każde wydanie", "es": "Cómo se revisa cada versión", "de": "Wie jede Version geprüft wird"},
"legal.downloads.check1": {
  "en": "Every package is built from the published source code by the project's own release scripts.",
  "pt-BR": "Cada pacote é gerado a partir do código-fonte publicado pelos próprios scripts de release do projeto.",
  "pl": "Każdy pakiet jest budowany z opublikowanego kodu źródłowego przez własne skrypty wydania projektu.",
  "es": "Cada paquete se genera a partir del código fuente publicado con los propios scripts de publicación del proyecto.",
  "de": "Jedes Paket wird mit den eigenen Release-Skripten des Projekts aus dem veröffentlichten Quellcode gebaut."},
"legal.downloads.check2": {
  "en": "After the build, the archives' contents are listed and compared, the program is started from the extracted files, and the SHA-256 checksums are published.",
  "pt-BR": "Após a geração, o conteúdo dos arquivos é listado e comparado, o programa é iniciado a partir dos arquivos extraídos e as somas SHA-256 são publicadas.",
  "pl": "Po zbudowaniu zawartość archiwów jest wypisywana i porównywana, program uruchamiany z rozpakowanych plików, a sumy SHA-256 publikowane.",
  "es": "Tras la compilación, se listan y comparan los contenidos de los archivos, se inicia el programa desde los archivos extraídos y se publican las sumas SHA-256.",
  "de": "Nach dem Bauen werden die Inhalte der Archive aufgelistet und verglichen, das Programm aus den entpackten Dateien gestartet und die SHA-256-Prüfsummen veröffentlicht."},
"legal.downloads.check3": {
  "en": "The program and installer are not code-signed and no third-party malware scan is published. Nobody can guarantee that a file is free of malware – scan it yourself.",
  "pt-BR": "O programa e o instalador não têm assinatura de código e nenhuma verificação de malware de terceiros é publicada. Ninguém pode garantir que um arquivo esteja livre de malware – verifique-o você mesmo.",
  "pl": "Program i instalator nie są podpisane cyfrowo, a zewnętrzny skan antywirusowy nie jest publikowany. Nikt nie może zagwarantować, że plik jest wolny od złośliwego oprogramowania – sprawdź go sam.",
  "es": "El programa y el instalador no tienen firma de código y no se publica ningún análisis de malware de terceros. Nadie puede garantizar que un archivo esté libre de malware: analízalo tú mismo.",
  "de": "Programm und Installer sind nicht signiert, und es wird keine Malware-Prüfung durch Dritte veröffentlicht. Niemand kann garantieren, dass eine Datei frei von Schadsoftware ist – prüfe sie selbst."},
"legal.downloads.check4": {
  "en": "Report anything suspicious to the responsible administrator by email.",
  "pt-BR": "Informe qualquer coisa suspeita ao administrador responsável por e-mail.",
  "pl": "Wszystko, co podejrzane, zgłoś e-mailem odpowiedzialnemu administratorowi.",
  "es": "Informa de cualquier cosa sospechosa al administrador responsable por correo.",
  "de": "Melde Verdächtiges per E-Mail an den verantwortlichen Administrator."},
"legal.downloads.purposeTitle": {"en": "What the program is for", "pt-BR": "Para que serve o programa", "pl": "Do czego służy program", "es": "Para qué sirve el programa", "de": "Wofür das Programm gedacht ist"},
"legal.downloads.purpose": {
  "en": "Tibia Foundry reads the files of the Tibia client on your own PC to make images, animations, maps and data for TibiaWiki and fansites. It is not a bot, cheat, macro or automation tool. Its file editors (Assets Editor, RCC Editor, Client Converter) never change the installed client in place and save copies – never play on Tibia's servers with modified client files, as that breaks the Tibia Rules.",
  "pt-BR": "O Tibia Foundry lê os arquivos do cliente do Tibia no seu próprio PC para criar imagens, animações, mapas e dados para a TibiaWiki e fansites. Não é bot, cheat, macro nem ferramenta de automação. Seus editores de arquivos (Assets Editor, RCC Editor, Client Converter) nunca alteram o cliente instalado e salvam cópias – nunca jogue nos servidores do Tibia com arquivos de cliente modificados, pois isso viola as Regras do Tibia.",
  "pl": "Tibia Foundry odczytuje pliki klienta Tibii na Twoim komputerze, aby tworzyć grafiki, animacje, mapy i dane dla TibiaWiki i fansite'ów. Nie jest botem, cheatem, makrem ani narzędziem do automatyzacji. Jego edytory plików (Assets Editor, RCC Editor, Client Converter) nigdy nie zmieniają zainstalowanego klienta i zapisują kopie – nigdy nie graj na serwerach Tibii ze zmodyfikowanymi plikami klienta, bo to łamie Zasady Tibii.",
  "es": "Tibia Foundry lee los archivos del cliente de Tibia en tu propio PC para crear imágenes, animaciones, mapas y datos para TibiaWiki y fansites. No es un bot, un cheat, una macro ni una herramienta de automatización. Sus editores de archivos (Assets Editor, RCC Editor, Client Converter) nunca modifican el cliente instalado y guardan copias: nunca juegues en los servidores de Tibia con archivos de cliente modificados, porque eso infringe las Reglas de Tibia.",
  "de": "Tibia Foundry liest die Dateien des Tibia-Clients auf deinem eigenen PC, um Bilder, Animationen, Karten und Daten für das TibiaWiki und Fansites zu erstellen. Es ist kein Bot, Cheat, Makro oder Automatisierungstool. Seine Datei-Editoren (Assets Editor, RCC Editor, Client Converter) ändern nie den installierten Client, sondern speichern Kopien – spiele nie mit veränderten Client-Dateien auf den Tibia-Servern, denn das verstößt gegen die Tibia-Regeln."},
"legal.money.title": {"en": "Money and advertising", "pt-BR": "Dinheiro e publicidade", "pl": "Pieniądze i reklamy", "es": "Dinero y publicidad", "de": "Geld und Werbung"},
"legal.money.adsTitle": {"en": "Advertising", "pt-BR": "Publicidade", "pl": "Reklamy", "es": "Publicidad", "de": "Werbung"},
"legal.money.ads": {
  "en": "No advertisements, sponsors or sponsored content.",
  "pt-BR": "Sem anúncios, patrocinadores ou conteúdo patrocinado.",
  "pl": "Brak reklam, sponsorów i treści sponsorowanych.",
  "es": "Sin anuncios, patrocinadores ni contenido patrocinado.",
  "de": "Keine Werbung, keine Sponsoren, keine gesponserten Inhalte."},
"legal.money.incomeTitle": {"en": "Income", "pt-BR": "Receita", "pl": "Dochody", "es": "Ingresos", "de": "Einnahmen"},
"legal.money.income": {
  "en": "No donations, affiliate links, paid features or other income. Tibia Foundry and this website are free; Rocky pays for them himself.",
  "pt-BR": "Sem doações, links de afiliados, recursos pagos ou outras receitas. O Tibia Foundry e este site são gratuitos; Rocky paga por eles do próprio bolso.",
  "pl": "Brak darowizn, linków afiliacyjnych, płatnych funkcji i innych dochodów. Tibia Foundry i ta strona są darmowe; Rocky pokrywa ich koszty sam.",
  "es": "Sin donaciones, enlaces de afiliados, funciones de pago ni otros ingresos. Tibia Foundry y este sitio son gratuitos; Rocky los paga de su bolsillo.",
  "de": "Keine Spenden, Affiliate-Links, kostenpflichtigen Funktionen oder sonstigen Einnahmen. Tibia Foundry und diese Website sind kostenlos; Rocky bezahlt sie selbst."},
"legal.money.hiddenTitle": {"en": "Nothing hidden", "pt-BR": "Nada escondido", "pl": "Nic ukrytego", "es": "Nada oculto", "de": "Nichts Verstecktes"},
"legal.money.hidden": {
  "en": "No crypto mining, no hidden use of your computer, no stream view-botting and no undisclosed tracking.",
  "pt-BR": "Sem mineração de criptomoedas, sem uso oculto do seu computador, sem inflar visualizações de lives e sem rastreamento não declarado.",
  "pl": "Brak kopania kryptowalut, ukrytego wykorzystywania Twojego komputera, sztucznego nabijania wyświetleń transmisji i nieujawnionego śledzenia.",
  "es": "Sin minería de criptomonedas, sin uso oculto de tu ordenador, sin inflar visualizaciones de streams y sin rastreo no declarado.",
  "de": "Kein Krypto-Mining, keine versteckte Nutzung deines Computers, kein Aufblähen von Stream-Zuschauerzahlen und kein verschwiegenes Tracking."},
"legal.money.change": {
  "en": "If this ever changes, it will be stated here before it starts.",
  "pt-BR": "Se isso mudar, será informado aqui antes de começar.",
  "pl": "Jeśli to się zmieni, zostanie to tu ogłoszone, zanim się zacznie.",
  "es": "Si esto cambia, se indicará aquí antes de que empiece.",
  "de": "Sollte sich das ändern, steht es hier, bevor es beginnt."},
"legal.privacy.title": {"en": "Privacy and statistics", "pt-BR": "Privacidade e estatísticas", "pl": "Prywatność i statystyki", "es": "Privacidad y estadísticas", "de": "Datenschutz und Statistik"},
"legal.privacy.intro": {
  "en": "Tibia Foundry has no accounts and collects no names, email addresses or passwords. Here is everything this website does with data.",
  "pt-BR": "O Tibia Foundry não tem contas e não coleta nomes, e-mails ou senhas. Aqui está tudo o que este site faz com dados.",
  "pl": "Tibia Foundry nie ma kont i nie zbiera imion, adresów e-mail ani haseł. Oto wszystko, co ta strona robi z danymi.",
  "es": "Tibia Foundry no tiene cuentas y no recopila nombres, correos ni contraseñas. Esto es todo lo que este sitio hace con los datos.",
  "de": "Tibia Foundry hat keine Konten und erfasst keine Namen, E-Mail-Adressen oder Passwörter. Das ist alles, was diese Website mit Daten macht."},
"legal.privacy.statsTitle": {"en": "Visit statistics (Google Analytics)", "pt-BR": "Estatísticas de visitas (Google Analytics)", "pl": "Statystyki odwiedzin (Google Analytics)", "es": "Estadísticas de visitas (Google Analytics)", "de": "Besuchsstatistik (Google Analytics)"},
"legal.privacy.stats": {
  "en": "This site uses Google Analytics 4 to count visits: pages viewed, approximate region, device and browser type, and clicks on download and GitHub links. Google sets cookies for this. Rocky sees only aggregated reports, and aggregated totals may be shared with CipSoft's fansite team. You can turn the statistics off for this browser:",
  "pt-BR": "Este site usa o Google Analytics 4 para contar visitas: páginas vistas, região aproximada, tipo de dispositivo e navegador e cliques em links de download e do GitHub. O Google define cookies para isso. Rocky vê apenas relatórios agregados, e totais agregados podem ser compartilhados com a equipe de fansites da CipSoft. Você pode desativar as estatísticas neste navegador:",
  "pl": "Ta strona używa Google Analytics 4 do liczenia odwiedzin: wyświetlone strony, przybliżony region, typ urządzenia i przeglądarki oraz kliknięcia w linki pobierania i GitHuba. Google zapisuje w tym celu pliki cookie. Rocky widzi tylko zbiorcze raporty, a zbiorcze liczby mogą trafić do zespołu fansite'ów CipSoft. Możesz wyłączyć statystyki w tej przeglądarce:",
  "es": "Este sitio usa Google Analytics 4 para contar visitas: páginas vistas, región aproximada, tipo de dispositivo y navegador, y clics en enlaces de descarga y de GitHub. Google coloca cookies para ello. Rocky solo ve informes agregados, y los totales agregados pueden compartirse con el equipo de fansites de CipSoft. Puedes desactivar las estadísticas en este navegador:",
  "de": "Diese Website zählt Besuche mit Google Analytics 4: aufgerufene Seiten, ungefähre Region, Geräte- und Browsertyp sowie Klicks auf Download- und GitHub-Links. Google setzt dafür Cookies. Rocky sieht nur zusammengefasste Berichte; zusammengefasste Summen können an das Fansite-Team von CipSoft gehen. Du kannst die Statistik für diesen Browser abschalten:"},
"legal.privacy.stateUnknown": {"en": "Turn on JavaScript to change this setting.", "pt-BR": "Ative o JavaScript para mudar esta configuração.", "pl": "Włącz JavaScript, aby zmienić to ustawienie.", "es": "Activa JavaScript para cambiar este ajuste.", "de": "Aktiviere JavaScript, um diese Einstellung zu ändern."},
"legal.privacy.turnOff": {"en": "Turn statistics off", "pt-BR": "Desativar estatísticas", "pl": "Wyłącz statystyki", "es": "Desactivar estadísticas", "de": "Statistik ausschalten"},
"legal.privacy.turnOn": {"en": "Turn statistics on", "pt-BR": "Ativar estatísticas", "pl": "Włącz statystyki", "es": "Activar estadísticas", "de": "Statistik einschalten"},
"legal.privacy.gpc": {
  "en": "If your browser sends a Global Privacy Control signal, statistics are off automatically.",
  "pt-BR": "Se o seu navegador enviar o sinal Global Privacy Control, as estatísticas ficam desativadas automaticamente.",
  "pl": "Jeśli Twoja przeglądarka wysyła sygnał Global Privacy Control, statystyki są wyłączone automatycznie.",
  "es": "Si tu navegador envía la señal Global Privacy Control, las estadísticas se desactivan automáticamente.",
  "de": "Sendet dein Browser ein Global-Privacy-Control-Signal, ist die Statistik automatisch aus."},
"legal.privacy.dataTitle": {"en": "Tibia data and images", "pt-BR": "Dados e imagens do Tibia", "pl": "Dane i obrazy Tibii", "es": "Datos e imágenes de Tibia", "de": "Tibia-Daten und Bilder"},
"legal.privacy.data": {
  "en": "Your browser asks the TibiaData API (api.tibiadata.com) for the Tibia data you view, including the characters you search for, and loads game images from static.tibia.com (CipSoft). Like every website, these services see your IP address.",
  "pt-BR": "Seu navegador pede à API do TibiaData (api.tibiadata.com) os dados do Tibia que você vê, incluindo os personagens que você pesquisa, e carrega imagens do jogo de static.tibia.com (CipSoft). Como qualquer site, esses serviços veem o seu endereço IP.",
  "pl": "Twoja przeglądarka pobiera oglądane dane Tibii, w tym wyszukiwane postacie, z API TibiaData (api.tibiadata.com), a obrazy z gry z static.tibia.com (CipSoft). Jak każda strona, te usługi widzą Twój adres IP.",
  "es": "Tu navegador pide a la API de TibiaData (api.tibiadata.com) los datos de Tibia que ves, incluidos los personajes que buscas, y carga las imágenes del juego desde static.tibia.com (CipSoft). Como cualquier sitio web, estos servicios ven tu dirección IP.",
  "de": "Dein Browser fragt die Tibia-Daten, die du ansiehst – auch gesuchte Charaktere –, bei der TibiaData-API (api.tibiadata.com) ab und lädt Spielbilder von static.tibia.com (CipSoft). Wie jede Website sehen diese Dienste deine IP-Adresse."},
"legal.privacy.storageTitle": {"en": "Stored in your browser", "pt-BR": "Guardado no seu navegador", "pl": "Zapisane w Twojej przeglądarce", "es": "Guardado en tu navegador", "de": "In deinem Browser gespeichert"},
"legal.privacy.storage": {
  "en": "Only on your device: your theme (tf-theme), language (tf-lang), statistics choice (tf-analytics) and last world viewed (tf-world), plus a cache of TibiaData answers that is deleted when you close the tab (tf-api). Nothing of it is sent to Tibia Foundry.",
  "pt-BR": "Somente no seu dispositivo: o tema (tf-theme), o idioma (tf-lang), a escolha sobre estatísticas (tf-analytics) e o último mundo visto (tf-world), além de um cache das respostas do TibiaData apagado ao fechar a aba (tf-api). Nada disso é enviado ao Tibia Foundry.",
  "pl": "Tylko na Twoim urządzeniu: motyw (tf-theme), język (tf-lang), wybór dotyczący statystyk (tf-analytics) i ostatnio oglądany świat (tf-world) oraz pamięć podręczna odpowiedzi TibiaData usuwana po zamknięciu karty (tf-api). Nic z tego nie trafia do Tibia Foundry.",
  "es": "Solo en tu dispositivo: el tema (tf-theme), el idioma (tf-lang), tu elección sobre estadísticas (tf-analytics) y el último mundo visto (tf-world), además de una caché de respuestas de TibiaData que se borra al cerrar la pestaña (tf-api). Nada de eso se envía a Tibia Foundry.",
  "de": "Nur auf deinem Gerät: dein Design (tf-theme), deine Sprache (tf-lang), deine Statistik-Wahl (tf-analytics) und die zuletzt angesehene Welt (tf-world), dazu ein Zwischenspeicher der TibiaData-Antworten, der beim Schließen des Tabs gelöscht wird (tf-api). Nichts davon geht an Tibia Foundry."},
"legal.privacy.nothingTitle": {"en": "What this site does not have", "pt-BR": "O que este site não tem", "pl": "Czego ta strona nie ma", "es": "Lo que este sitio no tiene", "de": "Was diese Website nicht hat"},
"legal.privacy.nothing": {
  "en": "No accounts, no contact form, no comments, no newsletter and no advertising trackers. Emails you send are read only by Rocky.",
  "pt-BR": "Sem contas, sem formulário de contato, sem comentários, sem newsletter e sem rastreadores de publicidade. Os e-mails que você envia são lidos apenas por Rocky.",
  "pl": "Brak kont, formularza kontaktowego, komentarzy, newslettera i reklamowych trackerów. Wysłane przez Ciebie e-maile czyta tylko Rocky.",
  "es": "Sin cuentas, sin formulario de contacto, sin comentarios, sin newsletter y sin rastreadores publicitarios. Los correos que envías solo los lee Rocky.",
  "de": "Keine Konten, kein Kontaktformular, keine Kommentare, kein Newsletter und keine Werbe-Tracker. E-Mails an Rocky liest nur Rocky."},
"legal.community.title": {"en": "Community rules", "pt-BR": "Regras da comunidade", "pl": "Zasady społeczności", "es": "Normas de la comunidad", "de": "Community-Regeln"},
"legal.community.text1": {
  "en": "This website has no forum, comments or uploads. The only public place to discuss Tibia Foundry is its GitHub issue tracker, which also follows GitHub's own rules.",
  "pt-BR": "Este site não tem fórum, comentários nem envio de arquivos. O único espaço público para discutir o Tibia Foundry é o rastreador de issues no GitHub, que também segue as regras do próprio GitHub.",
  "pl": "Ta strona nie ma forum, komentarzy ani możliwości przesyłania plików. Jedynym publicznym miejscem dyskusji o Tibia Foundry jest lista zgłoszeń na GitHubie, która podlega też zasadom GitHuba.",
  "es": "Este sitio no tiene foro, comentarios ni subida de archivos. El único espacio público para hablar de Tibia Foundry es su gestor de issues en GitHub, que además sigue las normas de GitHub.",
  "de": "Diese Website hat kein Forum, keine Kommentare und keine Uploads. Der einzige öffentliche Ort für Tibia Foundry ist der Issue-Tracker auf GitHub, für den zusätzlich die Regeln von GitHub gelten."},
"legal.community.text2": {
  "en": "Everything published here and in the project's GitHub space must respect the Tibia Rules. Not allowed:",
  "pt-BR": "Tudo o que é publicado aqui e no espaço do projeto no GitHub deve respeitar as Regras do Tibia. Não é permitido:",
  "pl": "Wszystko, co publikowane jest tutaj i w przestrzeni projektu na GitHubie, musi przestrzegać Zasad Tibii. Niedozwolone są:",
  "es": "Todo lo que se publica aquí y en el espacio del proyecto en GitHub debe respetar las Reglas de Tibia. No se permite:",
  "de": "Alles, was hier und im GitHub-Bereich des Projekts veröffentlicht wird, muss die Tibia-Regeln einhalten. Nicht erlaubt sind:"},
"legal.community.rule1": {"en": "Spam, harassment, abuse or offensive content.", "pt-BR": "Spam, assédio, abuso ou conteúdo ofensivo.", "pl": "Spam, nękanie, obelgi i obraźliwe treści.", "es": "Spam, acoso, abusos o contenido ofensivo.", "de": "Spam, Belästigung, Beleidigungen oder anstößige Inhalte."},
"legal.community.rule2": {"en": "Phishing, hack links, cheat tools, bots or advertising for them.", "pt-BR": "Phishing, links de hack, cheats, bots ou propaganda deles.", "pl": "Phishing, linki do hacków, cheaty, boty i ich reklamy.", "es": "Phishing, enlaces de hacks, cheats, bots o publicidad de ellos.", "de": "Phishing, Hack-Links, Cheat-Tools, Bots oder Werbung dafür."},
"legal.community.rule3": {"en": "Account trading, or trading characters, items or gold for real money.", "pt-BR": "Venda de contas ou troca de personagens, itens ou gold por dinheiro real.", "pl": "Handel kontami albo postaciami, przedmiotami lub złotem za prawdziwe pieniądze.", "es": "Comercio de cuentas o de personajes, objetos u oro por dinero real.", "de": "Account-Handel oder der Handel mit Charakteren, Gegenständen oder Gold gegen echtes Geld."},
"legal.community.report": {"en": "To report a problem with this website or the project, write to", "pt-BR": "Para denunciar um problema com este site ou o projeto, escreva para", "pl": "Aby zgłosić problem ze stroną lub projektem, napisz na", "es": "Para informar de un problema con este sitio o el proyecto, escribe a", "de": "Um ein Problem mit dieser Website oder dem Projekt zu melden, schreibe an"},
"legal.community.spoilersTitle": {"en": "Quest spoilers", "pt-BR": "Spoilers de quests", "pl": "Spoilery questów", "es": "Spoilers de quests", "de": "Quest-Spoiler"},
"legal.community.spoilers": {
  "en": "This website publishes no quest solutions. If complete solutions are ever added, they will appear only after this warning:",
  "pt-BR": "Este site não publica soluções de quests. Se soluções completas forem adicionadas algum dia, elas só aparecerão depois deste aviso:",
  "pl": "Ta strona nie publikuje rozwiązań questów. Jeśli kiedyś pojawią się pełne rozwiązania, zostaną pokazane dopiero po tym ostrzeżeniu:",
  "es": "Este sitio no publica soluciones de quests. Si alguna vez se añaden soluciones completas, solo aparecerán después de este aviso:",
  "de": "Diese Website veröffentlicht keine Quest-Lösungen. Sollten je vollständige Lösungen hinzukommen, erscheinen sie erst nach dieser Warnung:"},
"legal.spoilerWarning.title": {"en": "Warning: Spoilers ahead!", "pt-BR": "Atenção: spoilers à frente!", "pl": "Uwaga: spoilery!", "es": "¡Atención: spoilers!", "de": "Achtung: Spoiler!"},
"legal.spoilerWarning.text": {
  "en": "The following section contains detailed information and solutions for this quest. Continue only if you are comfortable with quest spoilers.",
  "pt-BR": "A seção a seguir contém informações detalhadas e soluções desta quest. Continue apenas se você não se importar com spoilers.",
  "pl": "Poniższa sekcja zawiera szczegółowe informacje i rozwiązania tego questa. Czytaj dalej tylko, jeśli nie przeszkadzają Ci spoilery.",
  "es": "La siguiente sección contiene información detallada y soluciones de esta quest. Continúa solo si no te importan los spoilers.",
  "de": "Der folgende Abschnitt enthält ausführliche Informationen und Lösungen zu dieser Quest. Lies nur weiter, wenn dich Spoiler nicht stören."},
"legal.sources.title": {"en": "Content sources", "pt-BR": "Fontes do conteúdo", "pl": "Źródła treści", "es": "Fuentes del contenido", "de": "Inhaltsquellen"},
"legal.sources.originalTitle": {"en": "Made by Tibia Foundry", "pt-BR": "Feito pelo Tibia Foundry", "pl": "Stworzone przez Tibia Foundry", "es": "Hecho por Tibia Foundry", "de": "Von Tibia Foundry"},
"legal.sources.original": {
  "en": "The texts of this website, the desktop program and its screenshots, and the pixel art of this site.",
  "pt-BR": "Os textos deste site, o programa para PC e suas capturas de tela, e a pixel art deste site.",
  "pl": "Teksty tej strony, program na PC i jego zrzuty ekranu oraz pixel art tej strony.",
  "es": "Los textos de este sitio, el programa de escritorio y sus capturas, y el pixel art de este sitio.",
  "de": "Die Texte dieser Website, das Desktop-Programm und seine Screenshots sowie die Pixel-Art dieser Seite."},
"legal.sources.officialTitle": {"en": "Official CipSoft information", "pt-BR": "Informações oficiais da CipSoft", "pl": "Oficjalne informacje CipSoft", "es": "Información oficial de CipSoft", "de": "Offizielle CipSoft-Informationen"},
"legal.sources.official": {
  "en": "News, characters, worlds, highscores, houses, creatures and spells come from Tibia.com and are © CipSoft GmbH; each view links to the original page.",
  "pt-BR": "Notícias, personagens, mundos, highscores, casas, criaturas e magias vêm do Tibia.com e são © CipSoft GmbH; cada página leva ao original.",
  "pl": "Wiadomości, postacie, światy, highscores, domy, stworzenia i czary pochodzą z Tibia.com i są © CipSoft GmbH; każdy widok prowadzi do oryginału.",
  "es": "Las noticias, personajes, mundos, highscores, casas, criaturas y hechizos vienen de Tibia.com y son © CipSoft GmbH; cada vista enlaza a la página original.",
  "de": "News, Charaktere, Welten, Highscores, Häuser, Kreaturen und Zauber stammen von Tibia.com und sind © CipSoft GmbH; jede Ansicht verlinkt das Original."},
"legal.sources.externalTitle": {"en": "External data", "pt-BR": "Dados externos", "pl": "Dane zewnętrzne", "es": "Datos externos", "de": "Externe Daten"},
"legal.sources.external": {
  "en": "TibiaData delivers the Tibia.com data as an API; every view names this source and shows when the data was fetched.",
  "pt-BR": "O TibiaData fornece os dados do Tibia.com como API; cada página cita essa fonte e mostra quando os dados foram obtidos.",
  "pl": "TibiaData udostępnia dane z Tibia.com jako API; każdy widok podaje to źródło i czas pobrania danych.",
  "es": "TibiaData ofrece los datos de Tibia.com como API; cada vista indica esta fuente y cuándo se obtuvieron los datos.",
  "de": "TibiaData stellt die Daten von Tibia.com als API bereit; jede Ansicht nennt diese Quelle und zeigt, wann die Daten abgerufen wurden."},
"legal.sources.communityTitle": {"en": "Community content", "pt-BR": "Conteúdo da comunidade", "pl": "Treści społeczności", "es": "Contenido de la comunidad", "de": "Community-Inhalte"},
"legal.sources.community": {
  "en": "Links to TibiaWiki and fansites lead to content owned by their authors; nothing of it is copied or translated here.",
  "pt-BR": "Os links para a TibiaWiki e fansites levam a conteúdos de seus autores; nada disso é copiado ou traduzido aqui.",
  "pl": "Linki do TibiaWiki i fansite'ów prowadzą do treści ich autorów; nic z nich nie jest tu kopiowane ani tłumaczone.",
  "es": "Los enlaces a TibiaWiki y fansites llevan a contenido de sus autores; nada de ello se copia ni se traduce aquí.",
  "de": "Links zum TibiaWiki und zu Fansites führen zu Inhalten ihrer Autoren; davon wird hier nichts kopiert oder übersetzt."},
"legal.sources.artTitle": {"en": "Third-party graphics", "pt-BR": "Gráficos de terceiros", "pl": "Grafiki innych podmiotów", "es": "Gráficos de terceros", "de": "Grafiken Dritter"},
"legal.sources.art": {
  "en": "Creature, item, outfit and house images are CipSoft's; the profile icons are the logos of their platforms.",
  "pt-BR": "As imagens de criaturas, itens, outfits e casas são da CipSoft; os ícones dos perfis são os logotipos de suas plataformas.",
  "pl": "Obrazy stworzeń, przedmiotów, strojów i domów należą do CipSoft; ikony profili to logotypy ich platform.",
  "es": "Las imágenes de criaturas, objetos, outfits y casas son de CipSoft; los iconos de los perfiles son los logotipos de sus plataformas.",
  "de": "Bilder von Kreaturen, Gegenständen, Outfits und Häusern gehören CipSoft; die Profil-Icons sind die Logos ihrer Plattformen."},
"legal.sources.unconfirmed": {
  "en": "Information not confirmed by CipSoft would be marked \"Unconfirmed information. This has not been confirmed by CipSoft.\" The site currently publishes no such information.",
  "pt-BR": "Informações não confirmadas pela CipSoft seriam marcadas como \"Informação não confirmada. Isto não foi confirmado pela CipSoft.\" No momento, o site não publica informações desse tipo.",
  "pl": "Informacje niepotwierdzone przez CipSoft byłyby oznaczone: \"Informacja niepotwierdzona. Nie została potwierdzona przez CipSoft.\" Obecnie strona nie publikuje takich informacji.",
  "es": "La información no confirmada por CipSoft se marcaría como \"Información no confirmada. CipSoft no ha confirmado esto.\" Actualmente el sitio no publica información de este tipo.",
  "de": "Von CipSoft nicht bestätigte Informationen würden als \"Unbestätigte Information. Dies wurde von CipSoft nicht bestätigt.\" gekennzeichnet. Derzeit veröffentlicht die Seite keine solchen Informationen."},
"about.character": {"en": "Tibia character: Roc ky", "pt-BR": "Personagem no Tibia: Roc ky", "pl": "Postać w Tibii: Roc ky", "es": "Personaje de Tibia: Roc ky", "de": "Tibia-Charakter: Roc ky"},
"about.adminNote": {
  "en": "Rocky is the responsible administrator of this website.",
  "pt-BR": "Rocky é o administrador responsável por este site.",
  "pl": "Rocky jest odpowiedzialnym administratorem tej strony.",
  "es": "Rocky es el administrador responsable de este sitio.",
  "de": "Rocky ist der verantwortliche Administrator dieser Website."},
"js.groupPlayers": {"en": "Players and guilds", "pt-BR": "Jogadores e guildas", "pl": "Gracze i gildie", "es": "Jugadores y guilds", "de": "Spieler und Gilden"},
"js.groupWorlds": {"en": "Worlds, rankings and houses", "pt-BR": "Mundos, rankings e casas", "pl": "Światy, rankingi i domy", "es": "Mundos, rankings y casas", "de": "Welten, Ranglisten und Häuser"},
"js.groupReference": {"en": "Creatures, bosses and spells", "pt-BR": "Criaturas, bosses e magias", "pl": "Stworzenia, bossowie i czary", "es": "Criaturas, jefes y hechizos", "de": "Kreaturen, Bosse und Zauber"},
"js.groupNews": {"en": "News and community", "pt-BR": "Notícias e comunidade", "pl": "Wiadomości i społeczność", "es": "Noticias y comunidad", "de": "News und Community"},
"js.analyticsOn": {"en": "Statistics are on for this browser.", "pt-BR": "As estatísticas estão ativadas neste navegador.", "pl": "Statystyki są włączone w tej przeglądarce.", "es": "Las estadísticas están activadas en este navegador.", "de": "Die Statistik ist für diesen Browser an."},
"js.analyticsOff": {"en": "Statistics are off for this browser.", "pt-BR": "As estatísticas estão desativadas neste navegador.", "pl": "Statystyki są wyłączone w tej przeglądarce.", "es": "Las estadísticas están desactivadas en este navegador.", "de": "Die Statistik ist für diesen Browser aus."},
"js.analyticsGpc": {
  "en": "Statistics are off: your browser sends a Global Privacy Control signal.",
  "pt-BR": "As estatísticas estão desativadas: seu navegador envia o sinal Global Privacy Control.",
  "pl": "Statystyki są wyłączone: Twoja przeglądarka wysyła sygnał Global Privacy Control.",
  "es": "Las estadísticas están desactivadas: tu navegador envía la señal Global Privacy Control.",
  "de": "Die Statistik ist aus: Dein Browser sendet ein Global-Privacy-Control-Signal."},
}


def set_path(tree, dotted, value):
    parts = dotted.split(".")
    for p in parts[:-1]:
        tree = tree.setdefault(p, {})
    tree[parts[-1]] = value


def del_path(tree, dotted):
    parts = dotted.split(".")
    for p in parts[:-1]:
        tree = tree.get(p, {})
    tree.pop(parts[-1], None)


def merge_texts():
    here = os.path.dirname(os.path.abspath(__file__))
    for code in ("en", "pt-BR", "pl", "es", "de"):
        path = os.path.join(here, code + ".json")
        data = json.load(open(path, encoding="utf-8"))
        for key in REMOVE:
            del_path(data, key)
        for key, values in T.items():
            set_path(data, key, values[code])
        open(path, "w", encoding="utf-8", newline="\n").write(json.dumps(data, ensure_ascii=False, indent=2) + "\n")
    print(f"{len(T)} texts in 5 languages; removed {', '.join(REMOVE)}")


if __name__ == "__main__":
    merge_texts()
