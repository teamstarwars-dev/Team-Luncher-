namespace TeamLauncher;

/// <summary>Tâche de fond visible dans le panneau de l'UI, annulable.</summary>
public sealed record AppTaskInfo(int Id, string Title, string Status);

/// <summary>
/// Registre des tâches de fond (imports, installations...) :
/// progression affichée dans le panneau de tâches du MainForm, bouton Annuler par tâche.
/// </summary>
public static class AppTasks
{
    private sealed class Entry
    {
        public required int Id;
        public required string Title;
        public string Status = "";
        public required CancellationTokenSource Cts;
    }

    private static readonly object Lock = new();
    private static readonly List<Entry> Items = new();
    private static int _nextId;

    /// <summary>Déclenché quand la liste/le statut change (thread quelconque).</summary>
    public static event Action? Changed;

    public static List<AppTaskInfo> Snapshot()
    {
        lock (Lock)
            return Items.Select(t => new AppTaskInfo(t.Id, t.Title, t.Status)).ToList();
    }

    public static void Cancel(int id)
    {
        Entry? entry;
        lock (Lock) entry = Items.FirstOrDefault(t => t.Id == id);
        if (entry != null) { try { entry.Cts.Cancel(); } catch { } }
    }

    /// <summary>
    /// Exécute une tâche de fond trackée. work(token, status) : status() met à jour le libellé.
    /// </summary>
    public static Task Run(string title, Func<CancellationToken, Action<string>, Task> work,
        Action<Exception>? onError = null)
    {
        int id;
        var cts = new CancellationTokenSource();
        var entry = new Entry { Id = 0, Title = title, Cts = cts, Status = Lang.T("Démarrage…", "Starting…") };
        lock (Lock)
        {
            id = ++_nextId;
            entry.Id = id;
            Items.Add(entry);
            Changed?.Invoke();
        }

        void SetStatus(string s)
        {
            entry.Status = s;
            Changed?.Invoke();
        }

        return Task.Run(async () =>
        {
            try
            {
                await work(cts.Token, SetStatus);
            }
            catch (OperationCanceledException)
            {
                GameLauncher.AppendLog($"Tâche annulée : {title}");
            }
            catch (Exception ex)
            {
                GameLauncher.AppendLog($"Tâche « {title} » échouée : {ex}");
                onError?.Invoke(ex);
            }
            finally
            {
                lock (Lock)
                {
                    Items.Remove(entry);
                    Changed?.Invoke();
                }
                cts.Dispose();
            }
        });
    }
}
