using System;

namespace StrongholdEuropeDE.Runtime
{
    internal sealed partial class DiplomacyFeature
    {
        // Internal transaction diagnostics stay in the log; the player gets an action.
        private string TradeFailureText(string reason)
        {
            if (reason == "native_busy" || reason == "native_save_or_trade_busy")
                return Text("Die Partie verarbeitet gerade eine andere Aktion. Versucht den Handel gleich erneut.", "The match is processing another action. Please try the trade again shortly.");
            if (reason == "market_changed_before_native_commit")
                return Text("Die Handelspreise haben sich geändert. Stellt ein neues Angebot zusammen.", "Market prices have changed. Please prepare a new offer.");
            if (reason == "stocks_changed_before_native_commit")
                return Text("Die Warenbestände haben sich geändert. Prüft die Mengen und sendet ein neues Angebot.", "Stocks have changed. Check the quantities and send a new offer.");
            if (reason == "native_rolled_back" || reason == "managed_commit_failed_native_rolled_back")
                return Text("Der Handel konnte nicht abgeschlossen werden. Alle Waren wurden zurückgegeben.", "The trade could not be completed. All goods were restored.");
            if (reason != null && (reason.IndexOf("quarantin", StringComparison.OrdinalIgnoreCase) >= 0 || reason.IndexOf("uncertain", StringComparison.OrdinalIgnoreCase) >= 0))
                return Text("Der Warenbestand konnte nicht eindeutig bestätigt werden. Weitere Handelsabschlüsse sind für diese Partie gesperrt.", "The stock balance could not be confirmed. Further trades are disabled for this match.");
            if (reason != null && reason.IndexOf("capacity", StringComparison.OrdinalIgnoreCase) >= 0)
                return Text("Der verfügbare Lagerplatz reicht nicht sicher aus. Verkleinert das Angebot oder schafft Lagerplatz.", "There is not enough confirmed storage space. Reduce the offer or make room.");
            if (reason == "accepted_trade_count_exhausted_before_native_write")
                return Text("Für diese Handelsbeziehung können keine weiteren Abschlüsse gespeichert werden.", "No further trades can be recorded for this trading relationship.");
            return Text("Der Handel wurde nicht abgeschlossen. Prüft die Handelsbeziehung, Warenbestände und Mengen.", "The trade was not completed. Check the trading relationship, stocks and quantities.");
        }
    }
}
