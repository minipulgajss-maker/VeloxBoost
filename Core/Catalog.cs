using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace VeloxBoost.Core
{
    /// <summary>
    /// Catálogo de otimizações. Para adicionar novas numa atualização basta acrescentar linhas aqui:
    /// os IDs são estáveis (categoria + nome em inglês), por isso o estado e os backups antigos continuam válidos.
    /// Nada aqui desliga Defender, UAC, Firewall, SmartScreen ou Windows Update.
    /// </summary>
    public static class Catalog
    {
        public static readonly List<Tweak> All = new();
        public static readonly string[] Categories =
            { "gaming", "network", "privacy", "performance", "interface", "ads", "power", "services", "tasks", "apps", "cleanup" };

        public static readonly Dictionary<string, string> Glyphs = new()
        {
            ["gaming"] = "", ["network"] = "", ["privacy"] = "", ["performance"] = "",
            ["interface"] = "", ["ads"] = "", ["power"] = "", ["services"] = "",
            ["tasks"] = "", ["apps"] = "", ["cleanup"] = ""
            ,["games"] = "\uE7B3"
        };

        const string LM = "HKLM", CU = "HKCU";
        const string Adv = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
        const string MMSys = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile";
        const string Tcp = @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters";
        const string GameDVR = @"Software\Microsoft\Windows\CurrentVersion\GameDVR";
        const string CDM = @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager";
        const string Consent = @"SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\";
        const string Edge = @"SOFTWARE\Policies\Microsoft\Edge";

        static string _cat;
        static readonly HashSet<string> Ids = new();

        static Tweak T(string pt, string en, Risk r, params TAction[] a)
        {
            string id = _cat + "." + Regex.Replace(en.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
            string baseId = id; int n = 2;
            while (!Ids.Add(id)) id = baseId + "-" + n++;
            var t = new Tweak { Id = id, Category = _cat, NamePt = pt, NameEn = en, Risk = r, Actions = a.ToList() };
            All.Add(t);
            return t;
        }

        static RegAction R(string hive, string path, string name, object v, bool onlyIfExists = false) => new(hive, path, name, () => v, onlyIfExists);
        static CmdAction C(string exe, string apply, string revert) => new(exe, apply, revert);
        static PowerAction P(string sub, string setting, int v) => new(sub, setting, v);

        public static void Build()
        {
            if (All.Count > 0) return;
            Gaming(); Extras(); Network(); Privacy(); Performance(); Interface(); Ads(); Power(); Services(); Tasks(); Apps(); Cleanup();
        }

        // ═════════════════════════ JOGOS & FPS ═════════════════════════
        static void Gaming()
        {
            _cat = "gaming";
            T("Ativar Modo de Jogo", "Enable Game Mode", Risk.Safe,
                R(CU, @"Software\Microsoft\GameBar", "AutoGameModeEnabled", 1), R(CU, @"Software\Microsoft\GameBar", "AllowAutoGameMode", 1));
            T("Desativar Game DVR", "Disable Game DVR", Risk.Safe,
                R(CU, @"System\GameConfigStore", "GameDVR_Enabled", 0), R(LM, @"SOFTWARE\Policies\Microsoft\Windows\GameDVR", "AllowGameDVR", 0));
            T("Desativar gravação em segundo plano", "Disable background recording", Risk.Safe, R(CU, GameDVR, "AppCaptureEnabled", 0));
            T("Desativar captura de áudio da Game Bar", "Disable Game Bar audio capture", Risk.Safe, R(CU, GameDVR, "AudioCaptureEnabled", 0));
            T("Desativar captura do cursor", "Disable cursor capture", Risk.Safe, R(CU, GameDVR, "CursorCaptureEnabled", 0));
            T("Desativar gravação do histórico", "Disable historical capture", Risk.Safe, R(CU, GameDVR, "HistoricalCaptureEnabled", 0));
            T("Desativar painel de dicas da Game Bar", "Disable Game Bar startup tips", Risk.Safe, R(CU, @"Software\Microsoft\GameBar", "ShowStartupPanel", 0));
            T("Botão Xbox não abre a Game Bar", "Xbox button doesn't open Game Bar", Risk.Safe, R(CU, @"Software\Microsoft\GameBar", "UseNexusForGameBarEnabled", 0));
            T("Agendamento de GPU acelerado por hardware", "Hardware-accelerated GPU scheduling", Risk.Moderate,
                R(LM, @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode", 2)).NeedsReboot = true;
            T("Otimizações para jogos em janela", "Optimizations for windowed games", Risk.Safe,
                R(CU, @"Software\Microsoft\DirectX\UserGpuPreferences", "DirectXUserGlobalSettings", "SwapEffectUpgradeEnable=1;"));
            T("Ecrã inteiro exclusivo (desativar FSO)", "Exclusive fullscreen (disable FSO)", Risk.Moderate,
                R(CU, @"System\GameConfigStore", "GameDVR_FSEBehaviorMode", 2), R(CU, @"System\GameConfigStore", "GameDVR_HonorUserFSEBehaviorMode", 1),
                R(CU, @"System\GameConfigStore", "GameDVR_FSEBehavior", 2), R(CU, @"System\GameConfigStore", "GameDVR_DXGIHonorFSEWindowsCompatible", 1));
            T("Responsividade do sistema para jogos (MMCSS)", "System responsiveness for games (MMCSS)", Risk.Safe, R(LM, MMSys, "SystemResponsiveness", 10));
            T("Remover limitação de rede multimédia", "Remove multimedia network throttling", Risk.Safe, R(LM, MMSys, "NetworkThrottlingIndex", unchecked((int)0xFFFFFFFF)));
            T("Prioridade de GPU máxima para jogos", "Max GPU priority for games", Risk.Safe, R(LM, MMSys + @"\Tasks\Games", "GPU Priority", 8));
            T("Prioridade de CPU alta para jogos", "High CPU priority for games", Risk.Safe, R(LM, MMSys + @"\Tasks\Games", "Priority", 6));
            T("Categoria de agendamento Alta para jogos", "High scheduling category for games", Risk.Safe, R(LM, MMSys + @"\Tasks\Games", "Scheduling Category", "High"));
            T("Prioridade de E/S alta para jogos", "High I/O priority for games", Risk.Safe, R(LM, MMSys + @"\Tasks\Games", "SFIO Priority", "High"));
            T("Desativar aceleração do rato", "Disable mouse acceleration", Risk.Safe,
                R(CU, @"Control Panel\Mouse", "MouseSpeed", "0"), R(CU, @"Control Panel\Mouse", "MouseThreshold1", "0"), R(CU, @"Control Panel\Mouse", "MouseThreshold2", "0"));
            T("Repetição de teclado mais rápida", "Faster keyboard repeat", Risk.Safe,
                R(CU, @"Control Panel\Keyboard", "KeyboardDelay", "0"), R(CU, @"Control Panel\Keyboard", "KeyboardSpeed", "31"));
            T("Desativar atalho das Teclas Fixas", "Disable Sticky Keys shortcut", Risk.Safe, R(CU, @"Control Panel\Accessibility\StickyKeys", "Flags", "506"));
            T("Desativar atalho das Teclas de Filtro", "Disable Filter Keys shortcut", Risk.Safe, R(CU, @"Control Panel\Accessibility\Keyboard Response", "Flags", "122"));
            T("Desativar atalho das Teclas de Alternar", "Disable Toggle Keys shortcut", Risk.Safe, R(CU, @"Control Panel\Accessibility\ToggleKeys", "Flags", "58"));
            T("Prioridade para a janela em primeiro plano", "Foreground window priority boost", Risk.Moderate,
                R(LM, @"SYSTEM\CurrentControlSet\Control\PriorityControl", "Win32PrioritySeparation", 38));
            T("Desativar limitação de energia (Power Throttling)", "Disable Power Throttling", Risk.Moderate,
                R(LM, @"SYSTEM\CurrentControlSet\Control\Power\PowerThrottling", "PowerThrottlingOff", 1));
            T("Desativar Multiplane Overlay (corrige cintilação)", "Disable Multiplane Overlay (fixes flicker)", Risk.Advanced,
                R(LM, @"SOFTWARE\Microsoft\Windows\Dwm", "OverlayTestMode", 5)).NeedsReboot = true;
            T("Desativar tick dinâmico (latência menor)", "Disable dynamic tick (lower latency)", Risk.Advanced,
                C("bcdedit.exe", "/set disabledynamictick yes", "/deletevalue disabledynamictick")).NeedsReboot = true;
        }

        // ═════════════════════════ REDE & PING ═════════════════════════
        static void Network()
        {
            _cat = "network";
            const string If = Tcp + @"\Interfaces\*";
            T("Confirmação TCP imediata (TcpAckFrequency)", "Immediate TCP ACK (TcpAckFrequency)", Risk.Safe, R(LM, If, "TcpAckFrequency", 1));
            T("Desativar algoritmo de Nagle (TCPNoDelay)", "Disable Nagle's algorithm (TCPNoDelay)", Risk.Safe, R(LM, If, "TCPNoDelay", 1));
            T("Atraso de ACK zero", "Zero delayed-ACK ticks", Risk.Safe, R(LM, If, "TcpDelAckTicks", 0));
            T("Auto-ajuste da janela TCP normal", "TCP window auto-tuning normal", Risk.Safe, C("netsh.exe", "int tcp set global autotuninglevel=normal", "int tcp set global autotuninglevel=normal"));
            T("Ativar Receive Side Scaling (RSS)", "Enable Receive Side Scaling (RSS)", Risk.Safe, C("netsh.exe", "int tcp set global rss=enabled", "int tcp set global rss=enabled"));
            T("Desativar RSC (menos latência)", "Disable RSC (lower latency)", Risk.Moderate, C("netsh.exe", "int tcp set global rsc=disabled", "int tcp set global rsc=enabled"));
            T("Desativar heurísticas de escala TCP", "Disable TCP scaling heuristics", Risk.Safe, C("netsh.exe", "int tcp set heuristics disabled", "int tcp set heuristics enabled"));
            T("Desativar timestamps TCP", "Disable TCP timestamps", Risk.Safe, C("netsh.exe", "int tcp set global timestamps=disabled", "int tcp set global timestamps=disabled"));
            T("RTO inicial mais rápido", "Faster initial RTO", Risk.Moderate, C("netsh.exe", "int tcp set global initialRto=2000", "int tcp set global initialRto=3000"));
            T("Ativar task offload", "Enable task offload", Risk.Safe, C("netsh.exe", "int ip set global taskoffload=enabled", "int ip set global taskoffload=enabled"));
            T("Controlo de congestionamento BBR2", "BBR2 congestion control", Risk.Advanced,
                C("netsh.exe", "int tcp set supplemental template=internet congestionprovider=bbr2", "int tcp set supplemental template=internet congestionprovider=cubic"));
            T("Desativar Teredo", "Disable Teredo", Risk.Advanced, C("netsh.exe", "interface teredo set state disabled", "interface teredo set state default"));
            T("Desativar ISATAP", "Disable ISATAP", Risk.Moderate, C("netsh.exe", "interface isatap set state disabled", "interface isatap set state default"));
            T("TTL padrão 64", "Default TTL 64", Risk.Safe, R(LM, Tcp, "DefaultTTL", 64));
            T("Ativar escala de janela TCP (RFC 1323)", "Enable TCP window scaling (RFC 1323)", Risk.Safe, R(LM, Tcp, "Tcp1323Opts", 1));
            T("Mais portas disponíveis", "More available ports", Risk.Safe, R(LM, Tcp, "MaxUserPort", 65534));
            T("Libertar portas mais depressa", "Release ports faster", Risk.Safe, R(LM, Tcp, "TcpTimedWaitDelay", 30));
            T("Retransmissão rápida TCP", "TCP fast retransmit", Risk.Safe, R(LM, Tcp, "TcpMaxDupAcks", 2));
            T("Ativar SACK", "Enable SACK", Risk.Safe, R(LM, Tcp, "SackOpts", 1));
            T("Sem largura de banda reservada para QoS", "No bandwidth reserved for QoS", Risk.Safe, R(LM, @"SOFTWARE\Policies\Microsoft\Windows\Psched", "NonBestEffortLimit", 0));
            T("Não guardar falhas de DNS em cache", "Don't cache failed DNS lookups", Risk.Safe, R(LM, @"SYSTEM\CurrentControlSet\Services\Dnscache\Parameters", "NegativeCacheTime", 0));
            T("Não guardar falhas de rede DNS", "Don't cache DNS network failures", Risk.Safe, R(LM, @"SYSTEM\CurrentControlSet\Services\Dnscache\Parameters", "NetFailureCacheTime", 0));
            T("Não guardar SOA negativo DNS", "Don't cache negative SOA", Risk.Safe, R(LM, @"SYSTEM\CurrentControlSet\Services\Dnscache\Parameters", "NegativeSOACacheTime", 0));
            T("DNS sobre HTTPS automático", "Automatic DNS over HTTPS", Risk.Safe, R(LM, @"SYSTEM\CurrentControlSet\Services\Dnscache\Parameters", "EnableAutoDoh", 2));
            T("Prioridade de resolução de nomes otimizada", "Optimized name resolution priority", Risk.Safe,
                R(LM, @"SYSTEM\CurrentControlSet\Services\Tcpip\ServiceProvider", "LocalPriority", 4), R(LM, @"SYSTEM\CurrentControlSet\Services\Tcpip\ServiceProvider", "HostsPriority", 5),
                R(LM, @"SYSTEM\CurrentControlSet\Services\Tcpip\ServiceProvider", "DnsPriority", 6), R(LM, @"SYSTEM\CurrentControlSet\Services\Tcpip\ServiceProvider", "NetbtPriority", 7));
            T("Sem limitação de largura de banda SMB", "No SMB bandwidth throttling", Risk.Safe, R(LM, @"SYSTEM\CurrentControlSet\Services\LanmanWorkstation\Parameters", "DisableBandwidthThrottling", 1));
            T("Desativar partilha P2P de atualizações", "Disable P2P update sharing", Risk.Safe, R(LM, @"SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization", "DODownloadMode", 0));
            T("Wi-Fi sem poupança de energia", "Wi-Fi without power saving", Risk.Safe, P("19cbb8fa-5279-450e-9fac-8a3d5fedd0c1", "12bbebe6-58d6-4636-95bb-3217ef867c1a", 0));
            T("Não ligar a hotspots Wi-Fi sugeridos", "Don't auto-connect to suggested hotspots", Risk.Safe,
                R(LM, @"SOFTWARE\Microsoft\PolicyManager\default\WiFi\AllowAutoConnectToWiFiSenseHotspots", "value", 0, true));
            T("Não reportar hotspots Wi-Fi", "Don't report Wi-Fi hotspots", Risk.Safe,
                R(LM, @"SOFTWARE\Microsoft\PolicyManager\default\WiFi\AllowWiFiHotSpotReporting", "value", 0, true));
            T("Desativar LLMNR (mais seguro e rápido)", "Disable LLMNR (safer and faster)", Risk.Safe, R(LM, @"SOFTWARE\Policies\Microsoft\Windows NT\DNSClient", "EnableMulticast", 0));
            T("Mais ligações simultâneas por servidor", "More simultaneous connections per server", Risk.Safe,
                R(CU, @"Software\Microsoft\Windows\CurrentVersion\Internet Settings", "MaxConnectionsPerServer", 16),
                R(CU, @"Software\Microsoft\Windows\CurrentVersion\Internet Settings", "MaxConnectionsPer1_0Server", 16));

            var a = T("Limpar cache DNS", "Flush DNS cache", Risk.Safe, C("ipconfig.exe", "/flushdns", null)); a.IsAction = true;
            a = T("Repor Winsock", "Reset Winsock", Risk.Moderate, C("netsh.exe", "winsock reset", null)); a.IsAction = true; a.NeedsReboot = true;
            a = T("Repor pilha TCP/IP", "Reset TCP/IP stack", Risk.Moderate, C("netsh.exe", "int ip reset", null)); a.IsAction = true; a.NeedsReboot = true;
            a = T("Registar DNS novamente", "Re-register DNS", Risk.Safe, C("ipconfig.exe", "/registerdns", null)); a.IsAction = true;
        }

        // ═════════════════════════ PRIVACIDADE ═════════════════════════
        static void Privacy()
        {
            _cat = "privacy";
            const string DC = @"SOFTWARE\Policies\Microsoft\Windows\DataCollection";
            T("Telemetria no mínimo", "Minimum telemetry", Risk.Safe, R(LM, DC, "AllowTelemetry", 0), R(LM, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\DataCollection", "AllowTelemetry", 0));
            T("Sem notificações de feedback", "No feedback notifications", Risk.Safe, R(LM, DC, "DoNotShowFeedbackNotifications", 1));
            T("Frequência de feedback: nunca", "Feedback frequency: never", Risk.Safe, R(CU, @"Software\Microsoft\Siuf\Rules", "NumberOfSIUFInPeriod", 0));
            T("Desativar ID de publicidade", "Disable advertising ID", Risk.Safe,
                R(CU, @"Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo", "Enabled", 0), R(LM, @"SOFTWARE\Policies\Microsoft\Windows\AdvertisingInfo", "DisabledByGroupPolicy", 1));
            T("Desativar experiências personalizadas", "Disable tailored experiences", Risk.Safe,
                R(CU, @"Software\Microsoft\Windows\CurrentVersion\Privacy", "TailoredExperiencesWithDiagnosticDataEnabled", 0),
                R(CU, @"Software\Policies\Microsoft\Windows\CloudContent", "DisableTailoredExperiencesWithDiagnosticData", 1));
            T("Desativar feed de atividade", "Disable activity feed", Risk.Safe, R(LM, @"SOFTWARE\Policies\Microsoft\Windows\System", "EnableActivityFeed", 0));
            T("Não publicar atividades do utilizador", "Don't publish user activities", Risk.Safe, R(LM, @"SOFTWARE\Policies\Microsoft\Windows\System", "PublishUserActivities", 0));
            T("Não enviar atividades do utilizador", "Don't upload user activities", Risk.Safe, R(LM, @"SOFTWARE\Policies\Microsoft\Windows\System", "UploadUserActivities", 0));
            T("Desativar área de transferência entre dispositivos", "Disable cross-device clipboard", Risk.Safe, R(LM, @"SOFTWARE\Policies\Microsoft\Windows\System", "AllowCrossDeviceClipboard", 0));
            T("Desativar reconhecimento de voz online", "Disable online speech recognition", Risk.Safe, R(CU, @"Software\Microsoft\Speech_OneCore\Settings\OnlineSpeechPrivacy", "HasAccepted", 0));
            T("Não recolher dados de escrita à mão", "Don't collect inking data", Risk.Safe, R(CU, @"Software\Microsoft\InputPersonalization", "RestrictImplicitInkCollection", 1));
            T("Não recolher dados de escrita", "Don't collect typing data", Risk.Safe, R(CU, @"Software\Microsoft\InputPersonalization", "RestrictImplicitTextCollection", 1));
            T("Não recolher contactos para escrita", "Don't harvest contacts", Risk.Safe, R(CU, @"Software\Microsoft\InputPersonalization\TrainedDataStore", "HarvestContacts", 0));
            T("Recusar política de personalização", "Decline personalization policy", Risk.Safe, R(CU, @"Software\Microsoft\Personalization\Settings", "AcceptedPrivacyPolicy", 0));
            T("Desativar estatísticas de escrita", "Disable typing insights", Risk.Safe, R(CU, @"Software\Microsoft\input\Settings", "InsightsEnabled", 0));
            T("Não rastrear apps abertas", "Don't track app launches", Risk.Safe, R(CU, Adv, "Start_TrackProgs", 0));
            T("Sites não acedem à lista de línguas", "Websites can't read language list", Risk.Safe, R(CU, @"Control Panel\International\User Profile", "HttpAcceptLanguageOptOut", 1));
            T("Desativar Relatório de Erros do Windows", "Disable Windows Error Reporting", Risk.Safe, R(LM, @"SOFTWARE\Microsoft\Windows\Windows Error Reporting", "Disabled", 1));
            T("Desativar Programa de Experiência do Cliente", "Disable Customer Experience Program", Risk.Safe, R(LM, @"SOFTWARE\Policies\Microsoft\SQMClient\Windows", "CEIPEnable", 0));
            T("Desativar telemetria de compatibilidade", "Disable app compatibility telemetry", Risk.Safe, R(LM, @"SOFTWARE\Policies\Microsoft\Windows\AppCompat", "AITEnable", 0));
            T("Desativar inventário de apps", "Disable app inventory", Risk.Safe, R(LM, @"SOFTWARE\Policies\Microsoft\Windows\AppCompat", "DisableInventory", 1));
            T("Desativar gravador de passos", "Disable Steps Recorder", Risk.Safe, R(LM, @"SOFTWARE\Policies\Microsoft\Windows\AppCompat", "DisableUAR", 1));
            T("Desativar Copilot", "Disable Copilot", Risk.Safe,
                R(CU, @"Software\Policies\Microsoft\Windows\WindowsCopilot", "TurnOffWindowsCopilot", 1), R(LM, @"SOFTWARE\Policies\Microsoft\Windows\WindowsCopilot", "TurnOffWindowsCopilot", 1));
            T("Desativar Recall (capturas de IA)", "Disable Recall (AI snapshots)", Risk.Safe,
                R(CU, @"Software\Policies\Microsoft\Windows\WindowsAI", "DisableAIDataAnalysis", 1), R(LM, @"SOFTWARE\Policies\Microsoft\Windows\WindowsAI", "DisableAIDataAnalysis", 1));
            T("Sem pesquisa Bing no Iniciar", "No Bing search in Start", Risk.Safe,
                R(CU, @"Software\Policies\Microsoft\Windows\Explorer", "DisableSearchBoxSuggestions", 1), R(CU, @"Software\Microsoft\Windows\CurrentVersion\Search", "BingSearchEnabled", 0));
            T("Desativar Cortana", "Disable Cortana", Risk.Safe, R(LM, @"SOFTWARE\Policies\Microsoft\Windows\Windows Search", "AllowCortana", 0));
            T("Desativar destaques da pesquisa", "Disable search highlights", Risk.Safe, R(CU, @"Software\Microsoft\Windows\CurrentVersion\SearchSettings", "IsDynamicSearchBoxEnabled", 0));
            T("Sem pesquisa na nuvem (conta Microsoft)", "No cloud search (Microsoft account)", Risk.Safe, R(CU, @"Software\Microsoft\Windows\CurrentVersion\SearchSettings", "IsMSACloudSearchEnabled", 0));
            T("Sem pesquisa na nuvem (conta escolar)", "No cloud search (work account)", Risk.Safe, R(CU, @"Software\Microsoft\Windows\CurrentVersion\SearchSettings", "IsAADCloudSearchEnabled", 0));
            T("Sem histórico de pesquisa no dispositivo", "No device search history", Risk.Safe, R(CU, @"Software\Microsoft\Windows\CurrentVersion\SearchSettings", "IsDeviceSearchHistoryEnabled", 0));
            T("Não partilhar dados de escrita à mão", "Don't share handwriting data", Risk.Safe, R(LM, @"SOFTWARE\Policies\Microsoft\Windows\TabletPC", "PreventHandwritingDataSharing", 1));
            T("Desativar Windows Spotlight", "Disable Windows Spotlight", Risk.Safe, R(CU, @"Software\Policies\Microsoft\Windows\CloudContent", "DisableWindowsSpotlightFeatures", 1));
            T("Desativar sincronização de mensagens", "Disable message sync", Risk.Safe, R(LM, @"SOFTWARE\Policies\Microsoft\Windows\Messaging", "AllowMessageSync", 0));
            T("Desativar telemetria de licenças KMS", "Disable KMS license telemetry", Risk.Safe, R(LM, @"SOFTWARE\Policies\Microsoft\Windows NT\CurrentVersion\Software Protection Platform", "NoGenTicket", 1));
            T("Desativar Localizar o meu dispositivo", "Disable Find My Device", Risk.Moderate, R(LM, @"SOFTWARE\Policies\Microsoft\FindMyDevice", "AllowFindMyDevice", 0));
            T("Bloquear acesso à localização", "Block location access", Risk.Moderate, R(LM, Consent + "location", "Value", "Deny"));
            T("Bloquear acesso a info da conta", "Block account info access", Risk.Moderate, R(LM, Consent + "userAccountInformation", "Value", "Deny"));
            T("Bloquear acesso aos contactos", "Block contacts access", Risk.Moderate, R(LM, Consent + "contacts", "Value", "Deny"));
            T("Bloquear acesso ao calendário", "Block calendar access", Risk.Moderate, R(LM, Consent + "appointments", "Value", "Deny"));
            T("Bloquear acesso ao histórico de chamadas", "Block call history access", Risk.Moderate, R(LM, Consent + "phoneCallHistory", "Value", "Deny"));
            T("Bloquear acesso ao e-mail", "Block email access", Risk.Moderate, R(LM, Consent + "email", "Value", "Deny"));
            T("Bloquear acesso às tarefas", "Block tasks access", Risk.Moderate, R(LM, Consent + "userDataTasks", "Value", "Deny"));
            T("Bloquear acesso às mensagens", "Block messaging access", Risk.Moderate, R(LM, Consent + "chat", "Value", "Deny"));
            T("Bloquear diagnóstico de outras apps", "Block app diagnostics access", Risk.Moderate, R(LM, Consent + "appDiagnostics", "Value", "Deny"));
            T("Desativar recolha de dados do Media Player", "Disable Media Player usage tracking", Risk.Safe, R(CU, @"Software\Microsoft\MediaPlayer\Preferences", "UsageTracking", 0));
            T("Desativar diagnóstico de escrita à mão", "Disable handwriting error reports", Risk.Safe, R(LM, @"SOFTWARE\Policies\Microsoft\Windows\HandwritingErrorReports", "PreventHandwritingErrorReports", 1));
            T("Desativar dados de localização para scripts", "Disable location scripting", Risk.Safe, R(LM, @"SOFTWARE\Policies\Microsoft\Windows\LocationAndSensors", "DisableLocationScripting", 1));
        }

        // ═════════════════════════ DESEMPENHO ═════════════════════════
        static void Performance()
        {
            _cat = "performance";
            const string MM = @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management";
            T("Desativar apps em segundo plano", "Disable background apps", Risk.Moderate, R(CU, @"Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications", "GlobalUserDisabled", 1));
            T("Kernel sempre na RAM", "Keep kernel in RAM", Risk.Moderate, R(LM, MM, "DisablePagingExecutive", 1)).NeedsReboot = true;
            T("Cache de sistema otimizada para apps", "System cache tuned for apps", Risk.Safe, R(LM, MM, "LargeSystemCache", 0));
            T("Agrupar processos svchost conforme a RAM", "Group svchost by installed RAM", Risk.Moderate,
                new RegAction(LM, @"SYSTEM\CurrentControlSet\Control", "SvcHostSplitThresholdInKB", () => (int)Math.Min(int.MaxValue, Native.TotalRamKb()))).NeedsReboot = true;
            T("Desativar Arranque Rápido (evita erros)", "Disable Fast Startup (avoids glitches)", Risk.Moderate, R(LM, @"SYSTEM\CurrentControlSet\Control\Session Manager\Power", "HiberbootEnabled", 0));
            T("Desativar Assistência Remota", "Disable Remote Assistance", Risk.Safe, R(LM, @"SYSTEM\CurrentControlSet\Control\Remote Assistance", "fAllowToGetHelp", 0));
            T("Desativar manutenção automática", "Disable automatic maintenance", Risk.Advanced, R(LM, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Schedule\Maintenance", "MaintenanceDisabled", 1));
            T("Sem reinício automático com sessão iniciada", "No auto-restart while signed in", Risk.Safe, R(LM, @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU", "NoAutoRebootWithLoggedOnUsers", 1));
            T("Não instalar drivers pelo Windows Update", "Don't install drivers via Windows Update", Risk.Advanced, R(LM, @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate", "ExcludeWUDriversInQualityUpdate", 1));
            T("Sem miniaturas em pastas de rede", "No thumbnails on network folders", Risk.Safe, R(CU, @"Software\Policies\Microsoft\Windows\Explorer", "DisableThumbsDBOnNetworkFolders", 1));
            T("Desativar data do último acesso NTFS", "Disable NTFS last-access time", Risk.Safe, C("fsutil.exe", "behavior set disablelastaccess 1", "behavior set disablelastaccess 2"));
            T("Desativar nomes 8.3 em NTFS", "Disable NTFS 8.3 names", Risk.Safe, C("fsutil.exe", "behavior set disable8dot3 1", "behavior set disable8dot3 2"));
            T("Mais memória para cache NTFS", "More memory for NTFS cache", Risk.Moderate, C("fsutil.exe", "behavior set memoryusage 2", "behavior set memoryusage 1"));
            T("Desativar hibernação (liberta GBs)", "Disable hibernation (frees GBs)", Risk.Moderate, C("powercfg.exe", "/hibernate off", "/hibernate on"));
            T("Ativar Sensor de Armazenamento", "Enable Storage Sense", Risk.Safe, R(CU, @"Software\Microsoft\Windows\CurrentVersion\StorageSense\Parameters\StoragePolicy", "01", 1));
            T("Ativar TRIM em SSD", "Enable SSD TRIM", Risk.Safe, C("fsutil.exe", "behavior set disabledeletenotify 0", "behavior set disabledeletenotify 0"));
            T("Mais rápido a terminar apps bloqueadas", "Kill hung apps faster", Risk.Safe, R(CU, @"Control Panel\Desktop", "HungAppTimeout", "2000"));
            T("Encerramento mais rápido de apps", "Faster app shutdown", Risk.Safe, R(CU, @"Control Panel\Desktop", "WaitToKillAppTimeout", "2000"));
            T("Encerramento mais rápido de serviços", "Faster service shutdown", Risk.Safe, R(LM, @"SYSTEM\CurrentControlSet\Control", "WaitToKillServiceTimeout", "2000"));
            T("Fechar apps automaticamente ao desligar", "Auto-end tasks on shutdown", Risk.Safe, R(CU, @"Control Panel\Desktop", "AutoEndTasks", "1"));
            T("Timeout de hooks de teclado/rato menor", "Lower input hook timeout", Risk.Safe, R(CU, @"Control Panel\Desktop", "LowLevelHooksTimeout", "1000"));
            T("Sem atraso das apps de arranque", "No startup apps delay", Risk.Safe, R(CU, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Serialize", "StartupDelayInMSec", 0));
            T("Explorador em processo separado", "Explorer in a separate process", Risk.Safe, R(CU, Adv, "SeparateProcess", 1));
            T("Pré-carregamento só de arranque", "Boot-only prefetching", Risk.Advanced, R(LM, MM + @"\PrefetchParameters", "EnablePrefetcher", 2));
            T("Não indexar ficheiros encriptados", "Don't index encrypted files", Risk.Safe, R(LM, @"SOFTWARE\Policies\Microsoft\Windows\Windows Search", "AllowIndexingEncryptedStoresOrItems", 0));
            T("Pesquisa não indexa com bateria", "No indexing on battery", Risk.Safe, R(LM, @"SOFTWARE\Policies\Microsoft\Windows\Windows Search", "PreventIndexOnBattery", 1));
        }

        // ═════════════════════════ INTERFACE ═════════════════════════
        static void Interface()
        {
            _cat = "interface";
            T("Menus abrem instantaneamente", "Instant menus", Risk.Safe, R(CU, @"Control Panel\Desktop", "MenuShowDelay", "0"));
            T("Sem animação ao minimizar", "No minimize animation", Risk.Safe, R(CU, @"Control Panel\Desktop\WindowMetrics", "MinAnimate", "0"));
            T("Sem animações na barra de tarefas", "No taskbar animations", Risk.Safe, R(CU, Adv, "TaskbarAnimations", 0));
            T("Sem sombras nos ícones", "No icon shadows", Risk.Safe, R(CU, Adv, "ListviewShadow", 0));
            T("Desativar Aero Peek", "Disable Aero Peek", Risk.Safe, R(CU, @"Software\Microsoft\Windows\DWM", "EnableAeroPeek", 0));
            T("Não guardar miniaturas da barra", "Don't hibernate taskbar thumbnails", Risk.Safe, R(CU, @"Software\Microsoft\Windows\DWM", "AlwaysHibernateThumbnails", 0));
            T("Efeitos visuais personalizados", "Custom visual effects", Risk.Safe, R(CU, @"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects", "VisualFXSetting", 3));
            T("Desativar transparência", "Disable transparency", Risk.Moderate, R(CU, @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "EnableTransparency", 0));
            T("Mostrar extensões de ficheiros", "Show file extensions", Risk.Safe, R(CU, Adv, "HideFileExt", 0));
            T("Mostrar ficheiros ocultos", "Show hidden files", Risk.Safe, R(CU, Adv, "Hidden", 1));
            T("Explorador abre em Este PC", "Explorer opens This PC", Risk.Safe, R(CU, Adv, "LaunchTo", 1));
            T("Menu de contexto clássico (Windows 11)", "Classic context menu (Windows 11)", Risk.Safe,
                R(CU, @"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32", "", ""));
            T("Esconder botão Vista de Tarefas", "Hide Task View button", Risk.Safe, R(CU, Adv, "ShowTaskViewButton", 0));
            T("Desativar Widgets", "Disable Widgets", Risk.Safe, R(LM, @"SOFTWARE\Policies\Microsoft\Dsh", "AllowNewsAndInterests", 0));
            T("Esconder Chat da barra", "Hide taskbar Chat", Risk.Safe, R(CU, Adv, "TaskbarMn", 0));
            T("Pesquisa só com ícone", "Search as icon only", Risk.Safe, R(CU, @"Software\Microsoft\Windows\CurrentVersion\Search", "SearchboxTaskbarMode", 1));
            T("Terminar tarefa na barra (botão direito)", "End task from taskbar", Risk.Safe, R(CU, Adv + @"\TaskbarDeveloperSettings", "TaskbarEndTask", 1));
            T("Sem ficheiros recentes no Acesso Rápido", "No recent files in Quick Access", Risk.Safe, R(CU, @"Software\Microsoft\Windows\CurrentVersion\Explorer", "ShowRecent", 0));
            T("Sem pastas frequentes no Acesso Rápido", "No frequent folders in Quick Access", Risk.Safe, R(CU, @"Software\Microsoft\Windows\CurrentVersion\Explorer", "ShowFrequent", 0));
            T("Caminho completo na barra de título", "Full path in title bar", Risk.Safe, R(CU, @"Software\Microsoft\Windows\CurrentVersion\Explorer\CabinetState", "FullPath", 1));
            T("Desativar Aero Shake", "Disable Aero Shake", Risk.Safe, R(CU, Adv, "DisallowShaking", 1));
            T("Modo escuro nas apps", "Dark mode for apps", Risk.Safe, R(CU, @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 0));
            T("Modo escuro no sistema", "Dark mode for system", Risk.Safe, R(CU, @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "SystemUsesLightTheme", 0));
            T("Dicas ao passar o rato mais rápidas", "Faster hover tooltips", Risk.Safe, R(CU, @"Control Panel\Mouse", "MouseHoverTime", "100"));
            T("Pré-visualização da barra mais rápida", "Faster taskbar previews", Risk.Safe, R(CU, Adv, "ExtendedUIHoverTime", 100));
            T("Sem sufixo \"- Atalho\"", "No \"- Shortcut\" suffix", Risk.Safe, R(CU, @"Software\Microsoft\Windows\CurrentVersion\Explorer\NamingTemplates", "ShortcutNameTemplate", "\"%s.lnk\""));
            T("Ecrã de início sem desfoque", "No blur on sign-in screen", Risk.Safe, R(LM, @"SOFTWARE\Policies\Microsoft\Windows\System", "DisableAcrylicBackgroundOnLogon", 1));
            T("Mostrar detalhes ao ligar/desligar", "Verbose startup/shutdown status", Risk.Safe, R(LM, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "VerboseStatus", 1));
            T("Texto ClearType nítido", "Crisp ClearType text", Risk.Safe, R(CU, @"Control Panel\Desktop", "FontSmoothing", "2"), R(CU, @"Control Panel\Desktop", "FontSmoothingType", 2));
            T("Esconder apps adicionadas recentemente", "Hide recently added apps", Risk.Safe, R(LM, @"SOFTWARE\Policies\Microsoft\Windows\Explorer", "HideRecentlyAddedApps", 1));
            T("NumLock ligado no arranque", "NumLock on at startup", Risk.Safe, R(CU, @"Control Panel\Keyboard", "InitialKeyboardIndicators", "2"));
            T("Barra de tarefas alinhada à esquerda", "Left-aligned taskbar", Risk.Moderate, R(CU, Adv, "TaskbarAl", 0));
            T("Mostrar segundos no relógio", "Show seconds on clock", Risk.Safe, R(CU, Adv, "ShowSecondsInSystemClock", 1));
            T("Sem documentos recentes no Iniciar", "No recent documents in Start", Risk.Safe, R(CU, Adv, "Start_TrackDocs", 0));
            T("Explorador sem cabeçalho Galeria/Início", "Compact Explorer view", Risk.Safe, R(CU, Adv, "UseCompactMode", 1));
            T("Mostrar caixas de seleção nos ficheiros", "Show file checkboxes", Risk.Safe, R(CU, Adv, "AutoCheckSelect", 1));
            T("Desativar som de arranque", "Disable startup sound", Risk.Safe, R(LM, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Authentication\LogonUI\BootAnimation", "DisableStartupSound", 1));
            T("Desativar barra de Snap no topo", "Disable top Snap bar", Risk.Safe, R(CU, Adv, "EnableSnapBar", 0));
        }

        // ═════════════════════════ ANÚNCIOS & SUGESTÕES ═════════════════════════
        static void Ads()
        {
            _cat = "ads";
            var cdm = new (string v, string pt, string en)[]
            {
                ("ContentDeliveryAllowed", "Bloquear entrega de conteúdo promocional", "Block promotional content delivery"),
                ("FeatureManagementEnabled", "Desativar gestão de funcionalidades promocionais", "Disable promotional feature management"),
                ("OemPreInstalledAppsEnabled", "Bloquear apps pré-instaladas do fabricante", "Block OEM pre-installed apps"),
                ("PreInstalledAppsEnabled", "Bloquear apps pré-instaladas", "Block pre-installed apps"),
                ("PreInstalledAppsEverEnabled", "Nunca reinstalar apps pré-instaladas", "Never re-enable pre-installed apps"),
                ("SilentInstalledAppsEnabled", "Bloquear instalação silenciosa de apps", "Block silent app installs"),
                ("RotatingLockScreenEnabled", "Sem imagens promocionais no ecrã de bloqueio", "No promo images on lock screen"),
                ("RotatingLockScreenOverlayEnabled", "Sem dicas no ecrã de bloqueio", "No tips on lock screen"),
                ("SoftLandingEnabled", "Sem dicas do Windows", "No Windows tips"),
                ("SubscribedContentEnabled", "Sem conteúdo subscrito", "No subscribed content"),
                ("SystemPaneSuggestionsEnabled", "Sem sugestões no Iniciar", "No Start suggestions"),
                ("SubscribedContent-310093Enabled", "Sem experiência de boas-vindas pós-update", "No post-update welcome experience"),
                ("SubscribedContent-314559Enabled", "Sem sugestões de apps (314559)", "No app suggestions (314559)"),
                ("SubscribedContent-338387Enabled", "Sem factos no ecrã de bloqueio", "No lock screen fun facts"),
                ("SubscribedContent-338388Enabled", "Sem sugestões ocasionais no Iniciar", "No occasional Start suggestions"),
                ("SubscribedContent-338389Enabled", "Sem truques e sugestões", "No tips and tricks"),
                ("SubscribedContent-338393Enabled", "Sem conteúdo sugerido nas Definições", "No suggested content in Settings"),
                ("SubscribedContent-353694Enabled", "Sem conteúdo sugerido nas Definições (2)", "No suggested content in Settings (2)"),
                ("SubscribedContent-353696Enabled", "Sem conteúdo sugerido nas Definições (3)", "No suggested content in Settings (3)"),
                ("SubscribedContent-353698Enabled", "Sem sugestões na linha do tempo", "No timeline suggestions"),
                ("SubscribedContent-88000326Enabled", "Sem destaques do Spotlight", "No Spotlight highlights"),
            };
            foreach (var c in cdm) T(c.pt, c.en, Risk.Safe, R(CU, CDM, c.v, 0));

            T("Sem \"Concluir configuração do dispositivo\"", "No \"Finish setting up your device\"", Risk.Safe, R(CU, @"Software\Microsoft\Windows\CurrentVersion\UserProfileEngagement", "ScoobeSystemSettingEnabled", 0));
            T("Sem anúncios do OneDrive no Explorador", "No OneDrive ads in Explorer", Risk.Safe, R(CU, Adv, "ShowSyncProviderNotifications", 0));
            T("Sem recomendações no Iniciar", "No Start recommendations", Risk.Safe, R(CU, Adv, "Start_IrisRecommendations", 0));
            T("Sem notificações da conta no Iniciar", "No account notifications in Start", Risk.Safe, R(CU, Adv, "Start_AccountNotifications", 0));
            T("Sem notificações no ecrã de bloqueio", "No notifications on lock screen", Risk.Safe, R(CU, @"Software\Microsoft\Windows\CurrentVersion\Notifications\Settings", "NOC_GLOBAL_SETTING_ALLOW_TOASTS_ABOVE_LOCK", 0));
            T("Sem sugestões de Ligação ao Telemóvel", "No Phone Link suggestions", Risk.Safe, R(CU, @"Software\Microsoft\Windows\CurrentVersion\Mobility", "OptedIn", 0));
            T("Desativar funcionalidades de consumidor", "Disable consumer features", Risk.Safe, R(LM, @"SOFTWARE\Policies\Microsoft\Windows\CloudContent", "DisableWindowsConsumerFeatures", 1));
            T("Desativar dicas pós-instalação", "Disable soft-landing tips", Risk.Safe, R(LM, @"SOFTWARE\Policies\Microsoft\Windows\CloudContent", "DisableSoftLanding", 1));
            T("Sem anúncios de conteúdo de terceiros", "No third-party suggestions", Risk.Safe, R(CU, @"Software\Policies\Microsoft\Windows\CloudContent", "DisableThirdPartySuggestions", 1));
            T("Iniciar com mais apps afixadas, menos recomendações", "Start: more pins, fewer recommendations", Risk.Safe, R(CU, Adv, "Start_Layout", 1));
        }

        // ═════════════════════════ ENERGIA ═════════════════════════
        static void Power()
        {
            _cat = "power";
            const string PROC = "54533251-82be-4824-96c1-47b60b740d00";
            T("Plano Desempenho Máximo", "Ultimate Performance plan", Risk.Moderate, UltimatePlan());
            T("Desativar suspensão seletiva USB", "Disable USB selective suspend", Risk.Safe, P("2a737441-1930-4402-8d77-b2bebba308a3", "48e6b7a6-50f5-4782-a5d4-53bb8f07e226", 0));
            T("Desativar poupança de energia PCIe", "Disable PCIe link power saving", Risk.Safe, P("501a4d13-42af-4429-9fd1-a8218c268e20", "ee12f906-d277-404b-b6da-e5fa1a576df5", 0));
            T("CPU mínimo a 100%", "Minimum CPU state 100%", Risk.Moderate, P(PROC, "893dee8e-2bef-41e0-89c6-b55d0929964c", 100));
            T("CPU máximo a 100%", "Maximum CPU state 100%", Risk.Safe, P(PROC, "bc5038f7-23e0-4960-96da-33abaf5935ec", 100));
            T("Desativar estacionamento de núcleos", "Disable core parking", Risk.Moderate, P(PROC, "0cc5b647-c1df-4637-891a-dec35c318583", 100));
            T("Turbo boost agressivo", "Aggressive turbo boost", Risk.Moderate, P(PROC, "be337238-0d82-4146-a960-4f3749d470c7", 2));
            T("Preferência de energia: desempenho", "Energy preference: performance", Risk.Moderate, P(PROC, "36687f9e-e3a5-4dbf-b1dc-15eb381c6863", 0));
            T("Disco nunca desliga", "Disk never turns off", Risk.Safe, P("0012ee47-9041-4b5d-9b77-535fba8b1442", "6738e2c4-e8a5-4a42-b16a-e040e769756e", 0));
            T("Desativar suspensão híbrida", "Disable hybrid sleep", Risk.Safe, P("238c9fa8-0aad-41ed-83f4-97be242c8f20", "94ac6d29-73ce-41a6-809f-6363ba21b47e", 0));
            T("Desativar temporizadores de ativação", "Disable wake timers", Risk.Safe, P("238c9fa8-0aad-41ed-83f4-97be242c8f20", "bd3b718a-0680-4d9d-8ab2-e1d2b4ac806d", 0));
            T("Desativar brilho adaptativo", "Disable adaptive brightness", Risk.Safe, P("7516b95f-f776-4464-8c53-06167f40cc99", "fbd9aa66-9553-4097-ba44-ed6e9d65eab8", 0));
            T("Política de arrefecimento ativa", "Active cooling policy", Risk.Safe, P(PROC, "94d3a615-a899-4ac5-ae2b-e4d8f634367f", 1));
            T("Desativar poupança de energia do ecrã em multimédia", "Multimedia: prevent idle to sleep", Risk.Safe, P("9596fb26-9850-41fd-ac3e-f7c3c00afd4b", "03680956-93bc-4294-bba6-4e0f09bb717f", 1));
            T("Reprodução de vídeo: otimizar qualidade", "Video playback: optimize quality", Risk.Safe, P("9596fb26-9850-41fd-ac3e-f7c3c00afd4b", "10778347-1370-4ee0-8bbd-33bdacaade49", 1));
        }

        static TAction UltimatePlan()
        {
            const string Ultimate = "e9a42b02-d5df-448d-aa00-03f14749eb61";
            const string Key = "pwr|plan";
            string Active()
            {
                var r = Sys.Run("powercfg.exe", "/getactivescheme");
                var m = Regex.Match(r.Output, @"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}");
                return m.Success ? m.Value.ToLowerInvariant() : null;
            }
            return new CustomAction("powercfg -duplicatescheme " + Ultimate,
                () => BackupStore.Get(Key) is { } e && Active() == e.Data?.Split('|').Last(),
                () =>
                {
                    string prev = Active();
                    var r = Sys.Run("powercfg.exe", "-duplicatescheme " + Ultimate);
                    var m = Regex.Match(r.Output, @"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}");
                    if (!m.Success) throw new Exception(Sys.Trim(r.Output));
                    var set = Sys.Run("powercfg.exe", "/setactive " + m.Value);
                    if (set.Code != 0) throw new Exception(Sys.Trim(set.Output));
                    var old = BackupStore.Get(Key);
                    BackupStore.Set(Key, new BackupEntry { Existed = true, Data = (old?.Data?.Split('|')[0] ?? prev) + "|" + m.Value.ToLowerInvariant() });
                },
                () =>
                {
                    var e = BackupStore.Get(Key);
                    if (e == null) return;
                    var p = e.Data.Split('|');
                    Sys.Run("powercfg.exe", "/setactive " + p[0]);
                    Sys.Run("powercfg.exe", "/delete " + p[1]);
                    BackupStore.Remove(Key);
                });
        }

        // ═════════════════════════ SERVIÇOS ═════════════════════════
        static void Services()
        {
            _cat = "services";
            // (nome, descrição, arranque 3=manual 4=desativado, risco)
            var list = new (string svc, string pt, string en, int start, Risk r)[]
            {
                ("DiagTrack", "Telemetria (Experiências do Utilizador Ligado)", "Telemetry (Connected User Experiences)", 4, Risk.Safe),
                ("dmwappushservice", "Encaminhamento de mensagens WAP", "WAP Push message routing", 4, Risk.Safe),
                ("WerSvc", "Relatório de Erros do Windows", "Windows Error Reporting", 3, Risk.Safe),
                ("wercplsupport", "Suporte do painel de relatórios de problemas", "Problem Reports control panel", 3, Risk.Safe),
                ("Fax", "Fax", "Fax", 4, Risk.Safe),
                ("RetailDemo", "Modo de demonstração de loja", "Retail Demo", 4, Risk.Safe),
                ("MapsBroker", "Gestor de mapas transferidos", "Downloaded Maps Manager", 4, Risk.Safe),
                ("lfsvc", "Geolocalização", "Geolocation", 3, Risk.Moderate),
                ("RemoteRegistry", "Registo Remoto", "Remote Registry", 4, Risk.Safe),
                ("RemoteAccess", "Encaminhamento e Acesso Remoto", "Routing and Remote Access", 4, Risk.Safe),
                ("TrkWks", "Cliente de rastreio de ligações", "Distributed Link Tracking", 3, Risk.Safe),
                ("WMPNetworkSvc", "Partilha de rede do Media Player", "Media Player Network Sharing", 4, Risk.Safe),
                ("wisvc", "Programa Windows Insider", "Windows Insider Service", 4, Risk.Moderate),
                ("PhoneSvc", "Serviço de telefone", "Phone Service", 3, Risk.Safe),
                ("Spooler", "Spooler de impressão (sem impressora)", "Print Spooler (no printer)", 3, Risk.Advanced),
                ("SysMain", "SysMain / Superfetch", "SysMain / Superfetch", 3, Risk.Moderate),
                ("WSearch", "Indexação da Pesquisa", "Windows Search indexing", 3, Risk.Moderate),
                ("XblAuthManager", "Xbox Live: autenticação", "Xbox Live Auth Manager", 3, Risk.Safe),
                ("XblGameSave", "Xbox Live: jogos guardados", "Xbox Live Game Save", 3, Risk.Safe),
                ("XboxNetApiSvc", "Xbox Live: rede", "Xbox Live Networking", 3, Risk.Safe),
                ("XboxGipSvc", "Acessórios Xbox", "Xbox Accessory Management", 3, Risk.Safe),
                ("diagnosticshub.standardcollector.service", "Recolha de diagnóstico", "Diagnostics Hub Collector", 4, Risk.Safe),
                ("DPS", "Política de diagnóstico", "Diagnostic Policy Service", 3, Risk.Moderate),
                ("WdiServiceHost", "Anfitrião de diagnóstico (serviço)", "Diagnostic Service Host", 3, Risk.Safe),
                ("WdiSystemHost", "Anfitrião de diagnóstico (sistema)", "Diagnostic System Host", 3, Risk.Safe),
                ("PcaSvc", "Assistente de compatibilidade", "Program Compatibility Assistant", 3, Risk.Safe),
                ("SEMgrSvc", "Pagamentos e NFC", "Payments and NFC", 3, Risk.Safe),
                ("SCardSvr", "Cartão inteligente", "Smart Card", 3, Risk.Safe),
                ("ScDeviceEnum", "Enumeração de cartões inteligentes", "Smart Card Device Enumeration", 3, Risk.Safe),
                ("SCPolicySvc", "Remoção de cartão inteligente", "Smart Card Removal Policy", 3, Risk.Safe),
                ("icssvc", "Hotspot móvel", "Mobile Hotspot", 3, Risk.Safe),
                ("lmhosts", "Auxiliar NetBIOS", "TCP/IP NetBIOS Helper", 3, Risk.Safe),
                ("CscService", "Ficheiros offline", "Offline Files", 4, Risk.Safe),
                ("AJRouter", "Router AllJoyn", "AllJoyn Router", 4, Risk.Safe),
                ("ALG", "Gateway de camada de aplicação", "Application Layer Gateway", 3, Risk.Safe),
                ("NetTcpPortSharing", "Partilha de portas Net.Tcp", "Net.Tcp Port Sharing", 4, Risk.Safe),
                ("WpcMonSvc", "Controlo parental", "Parental Controls", 3, Risk.Safe),
                ("SensorDataService", "Dados de sensores", "Sensor Data Service", 3, Risk.Safe),
                ("SensrSvc", "Monitorização de sensores", "Sensor Monitoring", 3, Risk.Safe),
                ("SensorService", "Serviço de sensores", "Sensor Service", 3, Risk.Moderate),
                ("TroubleshootingSvc", "Resolução de problemas recomendada", "Recommended Troubleshooting", 3, Risk.Safe),
                ("diagsvc", "Execução de diagnósticos", "Diagnostic Execution", 3, Risk.Safe),
                ("Wecsvc", "Recolha de eventos do Windows", "Windows Event Collector", 3, Risk.Safe),
                ("WalletService", "Carteira", "Wallet Service", 3, Risk.Safe),
                ("fhsvc", "Histórico de Ficheiros", "File History", 3, Risk.Safe),
                ("edgeupdate", "Atualização do Edge", "Edge Update", 3, Risk.Safe),
                ("edgeupdatem", "Atualização do Edge (M)", "Edge Update (M)", 3, Risk.Safe),
                ("MicrosoftEdgeElevationService", "Elevação do Edge", "Edge Elevation Service", 3, Risk.Safe),
                ("gupdate", "Atualização do Google", "Google Update", 3, Risk.Safe),
                ("gupdatem", "Atualização do Google (M)", "Google Update (M)", 3, Risk.Safe),
                ("ssh-agent", "Agente OpenSSH", "OpenSSH Agent", 4, Risk.Safe),
                ("workfolderssvc", "Pastas de Trabalho", "Work Folders", 3, Risk.Safe),
                ("CDPSvc", "Plataforma de dispositivos ligados", "Connected Devices Platform", 3, Risk.Moderate),
                ("TapiSrv", "Telefonia", "Telephony", 3, Risk.Safe),
                ("SharedRealitySvc", "Dados espaciais", "Spatial Data Service", 3, Risk.Safe),
                ("spectrum", "Perceção do Windows", "Windows Perception", 3, Risk.Safe),
                ("perceptionsimulation", "Simulação de perceção", "Perception Simulation", 3, Risk.Safe),
                ("MixedRealityOpenXRSvc", "Realidade Mista OpenXR", "Mixed Reality OpenXR", 3, Risk.Safe),
                ("AppVClient", "Cliente App-V", "App-V Client", 4, Risk.Safe),
                ("UevAgentService", "Virtualização da experiência do utilizador", "User Experience Virtualization", 4, Risk.Safe),
                ("shpamsvc", "Gestão de contas partilhadas", "Shared PC Account Manager", 4, Risk.Safe),
                ("MSiSCSI", "Iniciador iSCSI", "iSCSI Initiator", 3, Risk.Safe),
                ("SNMPTRAP", "Traps SNMP", "SNMP Trap", 3, Risk.Safe),
                ("lltdsvc", "Mapeador de topologia de rede", "Link-Layer Topology Discovery", 3, Risk.Safe),
                ("autotimesvc", "Hora da rede móvel", "Cellular Time", 3, Risk.Safe),
                ("BDESVC", "BitLocker (sem BitLocker ativo)", "BitLocker (when not used)", 3, Risk.Advanced),
                ("TabletInputService", "Teclado tátil (PC sem ecrã tátil)", "Touch Keyboard (non-touch PC)", 3, Risk.Advanced),
                ("WbioSrvc", "Biometria (sem Windows Hello)", "Biometrics (no Windows Hello)", 3, Risk.Advanced),
            };
            foreach (var s in list)
            {
                string mode = s.start == 4 ? " → Desativado" : " → Manual";
                string modeEn = s.start == 4 ? " → Disabled" : " → Manual";
                T(s.pt + mode, s.en + modeEn, s.r, R(LM, @"SYSTEM\CurrentControlSet\Services\" + s.svc, "Start", s.start, true)).NeedsReboot = true;
            }
        }

        // ═════════════════════════ TAREFAS AGENDADAS ═════════════════════════
        static void Tasks()
        {
            _cat = "tasks";
            var tasks = new (string path, Risk r)[]
            {
                (@"\Microsoft\Windows\Application Experience\Microsoft Compatibility Appraiser", Risk.Safe),
                (@"\Microsoft\Windows\Application Experience\ProgramDataUpdater", Risk.Safe),
                (@"\Microsoft\Windows\Application Experience\StartupAppTask", Risk.Safe),
                (@"\Microsoft\Windows\Application Experience\PcaPatchDbTask", Risk.Safe),
                (@"\Microsoft\Windows\Application Experience\MareBackup", Risk.Safe),
                (@"\Microsoft\Windows\Autochk\Proxy", Risk.Safe),
                (@"\Microsoft\Windows\Customer Experience Improvement Program\Consolidator", Risk.Safe),
                (@"\Microsoft\Windows\Customer Experience Improvement Program\UsbCeip", Risk.Safe),
                (@"\Microsoft\Windows\Customer Experience Improvement Program\KernelCeipTask", Risk.Safe),
                (@"\Microsoft\Windows\DiskDiagnostic\Microsoft-Windows-DiskDiagnosticDataCollector", Risk.Safe),
                (@"\Microsoft\Windows\Feedback\Siuf\DmClient", Risk.Safe),
                (@"\Microsoft\Windows\Feedback\Siuf\DmClientOnScenarioDownload", Risk.Safe),
                (@"\Microsoft\Windows\Windows Error Reporting\QueueReporting", Risk.Safe),
                (@"\Microsoft\Windows\Maps\MapsUpdateTask", Risk.Safe),
                (@"\Microsoft\Windows\Maps\MapsToastTask", Risk.Safe),
                (@"\Microsoft\Windows\CloudExperienceHost\CreateObjectTask", Risk.Safe),
                (@"\Microsoft\Windows\NetTrace\GatherNetworkInfo", Risk.Safe),
                (@"\Microsoft\Windows\Power Efficiency Diagnostics\AnalyzeSystem", Risk.Safe),
                (@"\Microsoft\Windows\Location\Notifications", Risk.Safe),
                (@"\Microsoft\Windows\Location\WindowsActionDialog", Risk.Safe),
                (@"\Microsoft\Windows\Shell\FamilySafetyMonitor", Risk.Safe),
                (@"\Microsoft\Windows\Shell\FamilySafetyRefreshTask", Risk.Safe),
                (@"\Microsoft\Windows\Device Information\Device", Risk.Safe),
                (@"\Microsoft\Windows\Device Information\Device User", Risk.Safe),
                (@"\Microsoft\Windows\Flighting\OneSettings\RefreshCache", Risk.Moderate),
                (@"\Microsoft\Windows\XblGameSave\XblGameSaveTask", Risk.Safe),
                (@"\Microsoft\Windows\Diagnosis\Scheduled", Risk.Moderate),
                (@"\Microsoft\Windows\Maintenance\WinSAT", Risk.Safe),
                (@"\Microsoft\Windows\WDI\ResolutionHost", Risk.Safe),
                (@"\Microsoft\Windows\Speech\SpeechModelDownloadTask", Risk.Safe),
                (@"\Microsoft\Windows\DiskFootprint\Diagnostics", Risk.Safe),
                (@"\Microsoft\Windows\PI\Sqm-Tasks", Risk.Safe),
                (@"\Microsoft\Windows\Feedback\Siuf\DmClient", Risk.Safe),
                (@"\Microsoft\Office\OfficeTelemetryAgentLogOn2016", Risk.Safe),
                (@"\Microsoft\Office\OfficeTelemetryAgentFallBack2016", Risk.Safe),
                (@"\Microsoft\Windows\Application Experience\SdbinstMergeDbTask", Risk.Moderate),
                (@"\Microsoft\Windows\Defrag\ScheduledDefrag", Risk.Advanced),
                (@"\Microsoft\Windows\Mobile Broadband Accounts\MNO Metadata Parser", Risk.Safe),
                (@"\Microsoft\Windows\Work Folders\Work Folders Logon Synchronization", Risk.Safe),
            };
            foreach (var t in tasks.DistinctBy(x => x.path))
            {
                string leaf = t.path.Substring(t.path.LastIndexOf('\\') + 1);
                T("Desativar tarefa: " + leaf, "Disable task: " + leaf, t.r, new TaskAction(t.path));
            }
        }

        // ═════════════════════════ APPS & BROWSERS ═════════════════════════
        static void Apps()
        {
            _cat = "apps";
            T("Edge: sem Arranque Rápido em segundo plano", "Edge: no Startup Boost", Risk.Safe, R(LM, Edge, "StartupBoostEnabled", 0));
            T("Edge: não correr em segundo plano", "Edge: no background mode", Risk.Safe, R(LM, Edge, "BackgroundModeEnabled", 0));
            T("Edge: esconder barra lateral", "Edge: hide sidebar", Risk.Safe, R(LM, Edge, "HubsSidebarEnabled", 0));
            T("Edge: sem recomendações", "Edge: no recommendations", Risk.Safe, R(LM, Edge, "ShowRecommendationsEnabled", 0));
            T("Edge: sem relatórios de personalização", "Edge: no personalization reporting", Risk.Safe, R(LM, Edge, "PersonalizationReportingEnabled", 0));
            T("Edge: dados de diagnóstico desligados", "Edge: diagnostic data off", Risk.Safe, R(LM, Edge, "DiagnosticData", 0));
            T("Edge: sem assistente de compras", "Edge: no shopping assistant", Risk.Safe, R(LM, Edge, "EdgeShoppingAssistantEnabled", 0));
            T("Edge: sem destaques e recomendações", "Edge: no spotlight recommendations", Risk.Safe, R(LM, Edge, "SpotlightExperiencesAndRecommendationsEnabled", 0));
            T("Edge: saltar primeira execução", "Edge: skip first-run experience", Risk.Safe, R(LM, Edge, "HideFirstRunExperience", 1));
            T("Edge: sem Microsoft Rewards", "Edge: no Microsoft Rewards", Risk.Safe, R(LM, Edge, "ShowMicrosoftRewards", 0));
            T("Edge: sem sugestões de seguir", "Edge: no follow suggestions", Risk.Safe, R(LM, Edge, "EdgeFollowEnabled", 0));
            T("Edge: sem pedidos de feedback", "Edge: no feedback prompts", Risk.Safe, R(LM, Edge, "UserFeedbackAllowed", 0));
            T("Edge: novo separador limpo", "Edge: clean new tab page", Risk.Safe, R(LM, Edge, "NewTabPageContentEnabled", 0));
            T("Edge: sem pré-carregamento do novo separador", "Edge: no new tab preload", Risk.Safe, R(LM, Edge, "NewTabPagePrerenderEnabled", 0));
            T("Edge: sem promoção do Edge como predefinido", "Edge: no default browser nag", Risk.Safe, R(LM, Edge, "DefaultBrowserSettingEnabled", 0));
            T("Chrome: não correr em segundo plano", "Chrome: no background mode", Risk.Safe, R(LM, @"SOFTWARE\Policies\Google\Chrome", "BackgroundModeEnabled", 0));
            T("Chrome: sem relatórios de métricas", "Chrome: no metrics reporting", Risk.Safe, R(LM, @"SOFTWARE\Policies\Google\Chrome", "MetricsReportingEnabled", 0));
            T("Chrome: sem relatórios da limpeza", "Chrome: no cleanup reporting", Risk.Safe, R(LM, @"SOFTWARE\Policies\Google\Chrome", "ChromeCleanupReportingEnabled", 0));
            T("Chrome: sem pedidos de feedback", "Chrome: no feedback prompts", Risk.Safe, R(LM, @"SOFTWARE\Policies\Google\Chrome", "UserFeedbackAllowed", 0));
            T("Firefox: sem telemetria", "Firefox: no telemetry", Risk.Safe, R(LM, @"SOFTWARE\Policies\Mozilla\Firefox", "DisableTelemetry", 1));
            T("Firefox: sem agente de browser predefinido", "Firefox: no default browser agent", Risk.Safe, R(LM, @"SOFTWARE\Policies\Mozilla\Firefox", "DisableDefaultBrowserAgent", 1));
            T("Firefox: sem estudos", "Firefox: no studies", Risk.Safe, R(LM, @"SOFTWARE\Policies\Mozilla\Firefox", "DisableFirefoxStudies", 1));
            T("Office: telemetria desligada", "Office: telemetry off", Risk.Safe, R(CU, @"Software\Policies\Microsoft\Office\16.0\Common\ClientTelemetry", "DisableTelemetry", 1));
            T("Office: não enviar telemetria", "Office: don't send telemetry", Risk.Safe, R(CU, @"Software\Policies\Microsoft\Office\16.0\Common\ClientTelemetry", "SendTelemetry", 3));
            T("Office: sem programa de melhoria", "Office: no improvement program", Risk.Safe, R(CU, @"Software\Policies\Microsoft\Office\16.0\Common", "QMEnable", 0));
            T("Office: sem feedback", "Office: no feedback", Risk.Safe, R(CU, @"Software\Policies\Microsoft\Office\16.0\Common\Feedback", "Enabled", 0));
            T("Office: sem registo de telemetria", "Office: no telemetry logging", Risk.Safe, R(CU, @"Software\Policies\Microsoft\Office\16.0\osm", "Enablelogging", 0));
            T("Office: sem envio de registos", "Office: no log upload", Risk.Safe, R(CU, @"Software\Policies\Microsoft\Office\16.0\osm", "EnableUpload", 0));
            T("Visual Studio: sem telemetria", "Visual Studio: no telemetry", Risk.Safe, R(LM, @"SOFTWARE\Policies\Microsoft\VisualStudio\SQM", "OptIn", 0));
            T(".NET CLI: sem telemetria", ".NET CLI: no telemetry", Risk.Safe, R(CU, "Environment", "DOTNET_CLI_TELEMETRY_OPTOUT", "1"));
            T("NVIDIA: sem telemetria", "NVIDIA: no telemetry", Risk.Safe, R(LM, @"SOFTWARE\NVIDIA Corporation\NvControlPanel2\Client", "OptInOrOutPreference", 0, true));
            T("Store: não transferir apps automaticamente", "Store: no automatic app downloads", Risk.Moderate, R(LM, @"SOFTWARE\Policies\Microsoft\WindowsStore", "AutoDownload", 2));
            T("OneDrive: desativar sincronização", "OneDrive: disable sync", Risk.Advanced, R(LM, @"SOFTWARE\Policies\Microsoft\Windows\OneDrive", "DisableFileSyncNGSC", 1));
            T("OneDrive: sem atualização antes de iniciar sessão", "OneDrive: no pre-sign-in network", Risk.Safe, R(LM, @"SOFTWARE\Microsoft\OneDrive", "PreventNetworkTrafficPreUserSignIn", 1));
        }

        // ═════════════════════════ LIMPEZA ═════════════════════════
        static void Cleanup()
        {
            _cat = "cleanup";
            void Cl(string pt, string en, Risk r, params CleanAction[] a) { var t = T(pt, en, r, a); t.IsAction = true; }
            Cl("Ficheiros temporários do utilizador", "User temp files", Risk.Safe, new CleanAction("%TEMP%"));
            Cl("Ficheiros temporários do Windows", "Windows temp files", Risk.Safe, new CleanAction(@"%WINDIR%\Temp"));
            Cl("Cache do Windows Update", "Windows Update cache", Risk.Moderate, new CleanAction(@"%WINDIR%\SoftwareDistribution\Download"));
            Cl("Cache de miniaturas", "Thumbnail cache", Risk.Safe, new CleanAction(@"%LOCALAPPDATA%\Microsoft\Windows\Explorer", "thumbcache_*.db"));
            Cl("Cache de ícones", "Icon cache", Risk.Safe, new CleanAction(@"%LOCALAPPDATA%\Microsoft\Windows\Explorer", "iconcache_*.db"));
            Cl("Despejos de falhas das apps", "App crash dumps", Risk.Safe, new CleanAction(@"%LOCALAPPDATA%\CrashDumps"));
            Cl("Minidumps do sistema", "System minidumps", Risk.Safe, new CleanAction(@"%WINDIR%\Minidump"), new CleanAction(@"%WINDIR%\LiveKernelReports", "*.dmp"));
            Cl("Relatórios de erros arquivados", "Archived error reports", Risk.Safe, new CleanAction(@"%PROGRAMDATA%\Microsoft\Windows\WER\ReportArchive"), new CleanAction(@"%PROGRAMDATA%\Microsoft\Windows\WER\ReportQueue"));
            Cl("Cache da Internet do Windows", "Windows internet cache", Risk.Safe, new CleanAction(@"%LOCALAPPDATA%\Microsoft\Windows\INetCache"));
            Cl("Cache de shaders DirectX", "DirectX shader cache", Risk.Safe, new CleanAction(@"%LOCALAPPDATA%\D3DSCache"));
            Cl("Cache de shaders NVIDIA", "NVIDIA shader cache", Risk.Safe, new CleanAction(@"%LOCALAPPDATA%\NVIDIA\DXCache"), new CleanAction(@"%LOCALAPPDATA%\NVIDIA\GLCache"));
            Cl("Cache de shaders AMD", "AMD shader cache", Risk.Safe, new CleanAction(@"%LOCALAPPDATA%\AMD\DxCache"), new CleanAction(@"%LOCALAPPDATA%\AMD\DxcCache"), new CleanAction(@"%LOCALAPPDATA%\AMD\VkCache"));
            Cl("Cache do Chrome", "Chrome cache", Risk.Safe, new CleanAction(@"%LOCALAPPDATA%\Google\Chrome\User Data\Default\Cache\Cache_Data"), new CleanAction(@"%LOCALAPPDATA%\Google\Chrome\User Data\Default\Code Cache"));
            Cl("Cache do Edge", "Edge cache", Risk.Safe, new CleanAction(@"%LOCALAPPDATA%\Microsoft\Edge\User Data\Default\Cache\Cache_Data"), new CleanAction(@"%LOCALAPPDATA%\Microsoft\Edge\User Data\Default\Code Cache"));
            Cl("Cache do Brave", "Brave cache", Risk.Safe, new CleanAction(@"%LOCALAPPDATA%\BraveSoftware\Brave-Browser\User Data\Default\Cache\Cache_Data"));
            Cl("Cache do Opera GX", "Opera GX cache", Risk.Safe, new CleanAction(@"%LOCALAPPDATA%\Opera Software\Opera GX Stable\Cache\Cache_Data"));
            Cl("Cache do Discord", "Discord cache", Risk.Safe, new CleanAction(@"%APPDATA%\discord\Cache\Cache_Data"), new CleanAction(@"%APPDATA%\discord\Code Cache"));
            Cl("Cache do Spotify", "Spotify cache", Risk.Safe, new CleanAction(@"%LOCALAPPDATA%\Spotify\Data"));
            Cl("Cache web da Steam", "Steam web cache", Risk.Safe, new CleanAction(@"%LOCALAPPDATA%\Steam\htmlcache"));
            Cl("Registos de instalação antigos", "Old setup logs", Risk.Safe, new CleanAction(@"%WINDIR%\Logs\CBS", "*.cab"), new CleanAction(@"%WINDIR%\Panther", "*.log"));
            Cl("Cache da Otimização de Entrega", "Delivery Optimization cache", Risk.Moderate,
                new CleanAction(@"%WINDIR%\ServiceProfiles\NetworkService\AppData\Local\Microsoft\Windows\DeliveryOptimization\Cache"));
            Cl("Ficheiros de pré-carregamento antigos", "Old prefetch files", Risk.Advanced, new CleanAction(@"%WINDIR%\Prefetch", "*.pf"));
        }

        // ═════════════════════════ PER-GAME ═════════════════════════
        const string IFEO = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\";
        const string GpuPref = @"Software\Microsoft\DirectX\UserGpuPreferences";
        const string Layers = @"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers";

        static string Newest(string root, string pattern, string exe)
        {
            root = Environment.ExpandEnvironmentVariables(root);
            if (!System.IO.Directory.Exists(root)) return null;
            return System.IO.Directory.GetDirectories(root, pattern).OrderByDescending(d => d)
                .Select(d => System.IO.Path.Combine(d, exe)).FirstOrDefault(System.IO.File.Exists);
        }

        static void Games()
        {
            _cat = "games";
            var games = new (string name, string exe, Func<string> find)[]
            {
                ("CS2", "cs2.exe", () => GameLocator.Steam(@"Counter-Strike Global Offensive\game\bin\win64\cs2.exe")),
                ("Valorant", "VALORANT-Win64-Shipping.exe", () => GameLocator.AnyDrive(@"Riot Games\VALORANT\live\ShooterGame\Binaries\Win64\VALORANT-Win64-Shipping.exe")),
                ("League of Legends", "League of Legends.exe", () => GameLocator.AnyDrive(@"Riot Games\League of Legends\Game\League of Legends.exe")),
                ("Fortnite", "FortniteClient-Win64-Shipping.exe", () => GameLocator.Epic("Fortnite", "FortniteClient-Win64-Shipping.exe")
                    ?? GameLocator.AnyDrive(@"Program Files\Epic Games\Fortnite\FortniteGame\Binaries\Win64\FortniteClient-Win64-Shipping.exe")),
                ("Apex Legends", "r5apex.exe", () => GameLocator.Steam(@"Apex Legends\r5apex.exe")),
                ("Rocket League", "RocketLeague.exe", () => GameLocator.Epic("Rocket League", "RocketLeague.exe") ?? GameLocator.Steam(@"rocketleague\Binaries\Win64\RocketLeague.exe")),
                ("Dota 2", "dota2.exe", () => GameLocator.Steam(@"dota 2 beta\game\bin\win64\dota2.exe")),
                ("Rust", "RustClient.exe", () => GameLocator.Steam(@"Rust\RustClient.exe")),
                ("PUBG", "TslGame.exe", () => GameLocator.Steam(@"PUBG\TslGame\Binaries\Win64\TslGame.exe")),
                ("Rainbow Six Siege", "RainbowSix.exe", () => GameLocator.Steam(@"Tom Clancy's Rainbow Six Siege\RainbowSix.exe")),
                ("GTA V", "GTA5.exe", () => GameLocator.Steam(@"Grand Theft Auto V\GTA5.exe") ?? GameLocator.Epic("Grand Theft Auto V", "GTA5.exe")),
                ("Overwatch 2", "Overwatch.exe", () => GameLocator.Steam(@"Overwatch\Overwatch.exe") ?? GameLocator.AnyDrive(@"Program Files (x86)\Overwatch\_retail_\Overwatch.exe")),
                ("Marvel Rivals", "Marvel-Win64-Shipping.exe", () => GameLocator.Steam(@"MarvelRivals\MarvelGame\Marvel\Binaries\Win64\Marvel-Win64-Shipping.exe")),
                ("Roblox", "RobloxPlayerBeta.exe", () => Newest(@"%LOCALAPPDATA%\Roblox\Versions", "version-*", "RobloxPlayerBeta.exe")),
            };
            foreach (var g in games)
            {
                var find = g.find; string exe = g.exe;
                T($"{g.name}: prioridade de CPU alta", $"{g.name}: high CPU priority", Risk.Moderate,
                    new GamePathAction(find, _ => new RegAction(LM, IFEO + exe + @"\PerfOptions", "CpuPriorityClass", () => 3)));
                T($"{g.name}: usar a GPU de alto desempenho", $"{g.name}: use high-performance GPU", Risk.Safe,
                    new GamePathAction(find, p => new RegAction(CU, GpuPref, p, () => "GpuPreference=2;")));
                T($"{g.name}: desativar otimizações de ecrã inteiro", $"{g.name}: disable fullscreen optimizations", Risk.Moderate,
                    new GamePathAction(find, p => new RegAction(CU, Layers, p, () => "~ DISABLEDXMAXIMIZEDWINDOWEDMODE")));
            }

            Func<string> discord = () => Newest(@"%LOCALAPPDATA%\Discord", "app-*", "Discord.exe");
            T("Discord: prioridade abaixo do normal (mais FPS)", "Discord: below-normal priority (more FPS)", Risk.Moderate,
                new GamePathAction(discord, _ => new RegAction(LM, IFEO + @"Discord.exe\PerfOptions", "CpuPriorityClass", () => 5)));
            T("Discord: usar GPU de poupança de energia", "Discord: use power-saving GPU", Risk.Safe,
                new GamePathAction(discord, p => new RegAction(CU, GpuPref, p, () => "GpuPreference=1;")));
            T("Discord: não iniciar com o Windows", "Discord: don't start with Windows", Risk.Safe,
                new GamePathAction(discord, _ => new RegAction(CU, @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run", "Discord",
                    () => new byte[] { 3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 })));
            Func<string> steamWeb = () => GameLocator.SteamLibraries().Select(l => System.IO.Path.Combine(l, @"bin\cef\cef.win7x64\steamwebhelper.exe")).FirstOrDefault(System.IO.File.Exists);
            T("Steam: navegador interno com prioridade baixa", "Steam: low priority web helper", Risk.Moderate,
                new GamePathAction(steamWeb, _ => new RegAction(LM, IFEO + @"steamwebhelper.exe\PerfOptions", "CpuPriorityClass", () => 5)));
            T("Steam: navegador interno usa GPU de poupança", "Steam: web helper uses power-saving GPU", Risk.Safe,
                new GamePathAction(steamWeb, p => new RegAction(CU, GpuPref, p, () => "GpuPreference=1;")));
            Func<string> obs = () => GameLocator.AnyDrive(@"Program Files\obs-studio\bin\64bit\obs64.exe");
            T("OBS: prioridade acima do normal", "OBS: above-normal priority (no dropped frames)", Risk.Safe,
                new GamePathAction(obs, _ => new RegAction(LM, IFEO + @"obs64.exe\PerfOptions", "CpuPriorityClass", () => 6)));
        }

        // ═════════════════════════ EXTRA (v1.1) ═════════════════════════
        static void Extras()
        {
            _cat = "network";
            T("Pacing TCP desligado", "TCP pacing off", Risk.Safe, C("netsh.exe", "int tcp set global pacingprofile=off", "int tcp set global pacingprofile=default"));
            T("Menos tentativas SYN", "Fewer SYN retries (faster failed connects)", Risk.Safe, C("netsh.exe", "int tcp set global maxsynretransmissions=2", "int tcp set global maxsynretransmissions=4"));
            T("Desativar resiliência RTT sem SACK", "Disable non-SACK RTT resiliency", Risk.Safe, C("netsh.exe", "int tcp set global nonsackrttresiliency=disabled", "int tcp set global nonsackrttresiliency=disabled"));
            _cat = "privacy";
            T("Desativar câmara no ecrã de bloqueio", "Disable lock screen camera", Risk.Safe, R(LM, @"SOFTWARE\Policies\Microsoft\Windows\Personalization", "NoLockScreenCamera", 1));
            T("Desativar Windows Ink Workspace", "Disable Windows Ink Workspace", Risk.Safe, R(LM, @"SOFTWARE\Policies\Microsoft\WindowsInkWorkspace", "AllowWindowsInkWorkspace", 0));
            _cat = "apps";
            T("Edge: sem atalho no ambiente de trabalho", "Edge: no desktop shortcut on update", Risk.Safe, R(LM, @"SOFTWARE\Policies\Microsoft\EdgeUpdate", "CreateDesktopShortcutDefault", 0));
            _cat = "interface";
            T("Mostrar Este PC no ambiente de trabalho", "Show This PC on desktop", Risk.Safe, R(CU, @"Software\Microsoft\Windows\CurrentVersion\Explorer\HideDesktopIcons\NewStartPanel", "{20D04FE0-3AEA-1069-A2D8-08002B30309D}", 0));
            T("Esconder ícone Saber mais sobre esta imagem", "Hide Learn about this picture icon", Risk.Safe, R(CU, @"Software\Microsoft\Windows\CurrentVersion\Explorer\HideDesktopIcons\NewStartPanel", "{2cc5ca98-6485-489a-920e-b3e88a6ccce3}", 1));
            T("Sem sugestões do Snap Assist", "No Snap Assist suggestions", Risk.Safe, R(CU, Adv, "SnapAssist", 0));
            _cat = "cleanup";
            var t = T("Cache do Microsoft Teams", "Microsoft Teams cache", Risk.Safe, new CleanAction(@"%APPDATA%\Microsoft\Teams\Cache"), new CleanAction(@"%APPDATA%\Microsoft\Teams\Service Worker\CacheStorage")); t.IsAction = true;
            t = T("Lista de ficheiros recentes", "Recent files list", Risk.Safe, new CleanAction(@"%APPDATA%\Microsoft\Windows\Recent", "*.lnk")); t.IsAction = true;
            t = T("Cache do Roblox", "Roblox cache", Risk.Safe, new CleanAction(@"%TEMP%\Roblox")); t.IsAction = true;
            t = T("Registos do Discord", "Discord logs", Risk.Safe, new CleanAction(@"%APPDATA%\discord\logs")); t.IsAction = true;
        }

        public static IEnumerable<Tweak> ForProfile(int profile) =>
            All.Where(t => (int)t.Risk <= profile);
    }
}
