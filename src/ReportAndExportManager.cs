using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace ClearVestige
{
    /// <summary>
    /// Générateur de Rapport HTML sombre autonome (façon SysPulse) et exportateur de script de réinstallation Winget.
    /// </summary>
    public static class ReportAndExportManager
    {
        public static void GenerateHtmlReport(string outputHtmlPath, List<InstalledApp> apps, List<HistoryEntry> history)
        {
            apps = apps ?? new List<InstalledApp>();
            history = history ?? new List<HistoryEntry>();

            long totalAppsBytes = apps.Sum(a => a.SizeBytes);
            long totalFreedBytes = history.Sum(h => h.FreedBytes);
            string nowStr = DateTime.Now.ToString("dd/MM/yyyy HH:mm");

            var sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE html>");
            sb.AppendLine("<html lang='fr'>");
            sb.AppendLine("<head>");
            sb.AppendLine("<meta charset='UTF-8'/>");
            sb.AppendLine("<meta name='viewport' content='width=device-width, initial-scale=1.0'/>");
            sb.AppendLine("<title>ClearVestige — Rapport Système & Inventaire</title>");
            sb.AppendLine("<style>");
            sb.AppendLine("body { font-family: 'Segoe UI', system-ui, sans-serif; background: #0B1120; color: #E2E8F0; margin: 0; padding: 28px; }");
            sb.AppendLine(".wrap { max-width: 1280px; margin: 0 auto; }");
            sb.AppendLine(".header { background: #111A2E; border: 1px solid #1E293B; border-radius: 12px; padding: 22px 26px; display: flex; justify-content: space-between; align-items: center; margin-bottom: 20px; }");
            sb.AppendLine(".header h1 { margin: 0; font-size: 22px; color: #FFFFFF; letter-spacing: 0.5px; }");
            sb.AppendLine(".header p { margin: 4px 0 0; color: #94A3B8; font-size: 13px; }");
            sb.AppendLine(".badge { background: #064E3B; border: 1px solid #10B981; color: #6EE7B7; padding: 6px 14px; border-radius: 20px; font-weight: 700; font-size: 12px; }");
            sb.AppendLine(".cards { display: grid; grid-template-columns: repeat(auto-fit, minmax(220px, 1fr)); gap: 14px; margin-bottom: 24px; }");
            sb.AppendLine(".card { background: #162032; border: 1px solid #26354D; border-radius: 10px; padding: 16px 20px; }");
            sb.AppendLine(".card .lbl { font-size: 11px; font-weight: 700; color: #94A3B8; letter-spacing: 0.6px; }");
            sb.AppendLine(".card .val { font-size: 24px; font-weight: 800; margin-top: 6px; }");
            sb.AppendLine(".c-blue { color: #38BDF8; } .c-purple { color: #A78BFA; } .c-green { color: #34D399; } .c-amber { color: #FBBF24; }");
            sb.AppendLine(".section { background: #111A2E; border: 1px solid #26354D; border-radius: 10px; padding: 18px 20px; margin-bottom: 22px; }");
            sb.AppendLine(".section h2 { margin: 0 0 12px; font-size: 16px; color: #F8FAFC; }");
            sb.AppendLine(".search { width: 100%; max-width: 420px; padding: 8px 12px; border-radius: 6px; border: 1px solid #334155; background: #0B1120; color: #F8FAFC; font-size: 13px; margin-bottom: 12px; }");
            sb.AppendLine("table { width: 100%; border-collapse: collapse; font-size: 12.5px; }");
            sb.AppendLine("th { background: #0B1120; color: #94A3B8; text-align: left; padding: 10px 12px; border-bottom: 1px solid #26354D; }");
            sb.AppendLine("td { padding: 9px 12px; border-bottom: 1px solid #1E293B; word-break: break-word; }");
            sb.AppendLine("tr:nth-child(even) td { background: #162032; }");
            sb.AppendLine("tr:hover td { background: #1E293B; }");
            sb.AppendLine(".footer { text-align: center; color: #64748B; font-size: 12px; margin-top: 16px; }");
            sb.AppendLine("</style>");
            sb.AppendLine("</head>");
            sb.AppendLine("<body><div class='wrap'>");

            sb.AppendLine("<div class='header'>");
            sb.AppendFormat("<div><h1>CLEARVESTIGE — Rapport d'Inventaire & Nettoyage</h1><p>Machine : {0} &bull; Généré le {1} &bull; ClearVestige v2.0</p></div>",
                HtmlEncode(Environment.MachineName), HtmlEncode(nowStr));
            sb.AppendLine("<div class='badge'>100% HORS-LIGNE • WINDOWS</div>");
            sb.AppendLine("</div>");

            sb.AppendLine("<div class='cards'>");
            sb.AppendFormat("<div class='card'><div class='lbl'>APPLICATIONS INSTALLÉES</div><div class='val c-blue'>{0}</div></div>", apps.Count);
            sb.AppendFormat("<div class='card'><div class='lbl'>ESPACE OCCUPÉ ESTIMÉ</div><div class='val c-purple'>{0}</div></div>", HtmlEncode(FormatUtils.FormatBytes(totalAppsBytes)));
            sb.AppendFormat("<div class='card'><div class='lbl'>NETTOYAGES EFFECTUÉS</div><div class='val c-amber'>{0}</div></div>", history.Count);
            sb.AppendFormat("<div class='card'><div class='lbl'>ESPACE TOTAL LIBÉRÉ</div><div class='val c-green'>{0}</div></div>", HtmlEncode(FormatUtils.FormatBytes(totalFreedBytes)));
            sb.AppendLine("</div>");

            if (history.Count > 0)
            {
                sb.AppendLine("<div class='section'><h2>Historique des nettoyages récents</h2><table><thead><tr><th>Date</th><th>Application</th><th>Éléments supprimés</th><th>Espace libéré</th></tr></thead><tbody>");
                foreach (var h in history.Take(50))
                {
                    sb.AppendFormat("<tr><td>{0}</td><td><b>{1}</b></td><td>{2}</td><td class='c-green'>{3}</td></tr>\n",
                        HtmlEncode(h.Timestamp), HtmlEncode(h.AppName), HtmlEncode(h.DetailsSummary), HtmlEncode(h.FreedFormatted));
                }
                sb.AppendLine("</tbody></table></div>");
            }

            sb.AppendLine("<div class='section'><h2>Inventaire des applications installées</h2>");
            sb.AppendLine("<input class='search' id='q' placeholder='Filtrer par nom, éditeur ou dossier...' oninput='filterApps()'/>");
            sb.AppendLine("<table id='appsTable'><thead><tr><th>Application</th><th>Éditeur</th><th>Version</th><th>Taille</th><th>Portée</th><th>Dossier</th></tr></thead><tbody>");
            foreach (var a in apps)
            {
                sb.AppendFormat("<tr><td><b>{0}</b></td><td>{1}</td><td>{2}</td><td>{3}</td><td>{4}</td><td>{5}</td></tr>\n",
                    HtmlEncode(a.DisplayName), HtmlEncode(a.Publisher), HtmlEncode(a.DisplayVersion),
                    HtmlEncode(a.SizeFormatted), HtmlEncode(a.ScopeLabel), HtmlEncode(a.InstallLocation));
            }
            sb.AppendLine("</tbody></table></div>");

            sb.AppendLine("<div class='footer'>Généré localement par ClearVestige — Aucune donnée envoyée sur Internet.</div>");
            sb.AppendLine("</div>");
            sb.AppendLine("<script>function filterApps(){var q=document.getElementById('q').value.toLowerCase();var rows=document.querySelectorAll('#appsTable tbody tr');for(var i=0;i<rows.length;i++){rows[i].style.display=rows[i].innerText.toLowerCase().indexOf(q)>=0?'':'none';}}</script>");
            sb.AppendLine("</body></html>");

            File.WriteAllText(outputHtmlPath, sb.ToString(), Encoding.UTF8);
        }

        /// <summary>
        /// Exporte un script PowerShell de réinstallation automatique via Winget (idéal avant réinstallation de Windows).
        /// Utilise 'winget list' pour récupérer les vrais identifiants de paquets Winget présents sur le PC.
        /// </summary>
        public static bool ExportWingetReinstallScript(string outputPs1Path, List<InstalledApp> fallbackApps, out string summaryMessage)
        {
            var wingetIds = new List<Tuple<string, string>>(); // (Name, Id)

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "winget.exe",
                    Arguments = "list --accept-source-agreements --source winget",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    StandardOutputEncoding = Encoding.UTF8
                };

                using (var p = Process.Start(psi))
                {
                    if (p != null)
                    {
                        string output = p.StandardOutput.ReadToEnd();
                        p.WaitForExit(15000);
                        foreach (var rawLine in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                        {
                            // Repérer un identifiant Winget typique (Editeur.Produit, ex: VideoLAN.VLC, Brave.Brave, Git.Git)
                            var m = Regex.Match(rawLine, @"^(.+?)\s{2,}([A-Za-z0-9\-]+\.[A-Za-z0-9\-\._]+)\s+");
                            if (m.Success)
                            {
                                string name = m.Groups[1].Value.Trim();
                                string id = m.Groups[2].Value.Trim();
                                if (id.Length >= 4 && !wingetIds.Any(x => string.Equals(x.Item2, id, StringComparison.OrdinalIgnoreCase)))
                                {
                                    wingetIds.Add(Tuple.Create(name, id));
                                }
                            }
                        }
                    }
                }
            }
            catch { }

            var sb = new StringBuilder();
            sb.AppendLine("# ============================================================================");
            sb.AppendLine("# Script de réinstallation automatique généré par ClearVestige");
            sb.AppendLine("# Machine : " + Environment.MachineName + " | Date : " + DateTime.Now.ToString("dd/MM/yyyy HH:mm"));
            sb.AppendLine("# Exécuter dans PowerShell : powershell -ExecutionPolicy Bypass -File .\\" + Path.GetFileName(outputPs1Path));
            sb.AppendLine("# ============================================================================");
            sb.AppendLine();

            if (wingetIds.Count > 0)
            {
                sb.AppendLine("Write-Host 'Installation des applications via Winget...' -ForegroundColor Cyan");
                foreach (var pair in wingetIds.OrderBy(x => x.Item1))
                {
                    sb.AppendFormat("# {0}\r\n", pair.Item1);
                    sb.AppendFormat("winget install --id \"{0}\" --exact --accept-package-agreements --accept-source-agreements\r\n", pair.Item2);
                }
                File.WriteAllText(outputPs1Path, sb.ToString(), new UTF8Encoding(true));
                summaryMessage = string.Format("Script Winget généré avec {0} identifiant(s) de paquets officiels détectés !", wingetIds.Count);
                return true;
            }
            else
            {
                sb.AppendLine("# Winget n'était pas disponible ou n'a retourné aucun paquet. Liste de vos applications :");
                if (fallbackApps != null)
                {
                    foreach (var app in fallbackApps.OrderBy(a => a.DisplayName))
                    {
                        sb.AppendFormat("# - {0} ({1})\r\n", app.DisplayName, app.Publisher);
                        sb.AppendFormat("# winget search \"{0}\"\r\n", (app.DisplayName ?? "").Replace("\"", ""));
                    }
                }
                File.WriteAllText(outputPs1Path, sb.ToString(), new UTF8Encoding(true));
                summaryMessage = "Winget n'a retourné aucun ID automatique : une liste commentée de vos applications a été exportée.";
                return true;
            }
        }

        private static string HtmlEncode(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;").Replace("'", "&#39;");
        }
    }
}
