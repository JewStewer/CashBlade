namespace Finora.Web.Services;

// Transient cross-component UI intent (e.g. "open the add-transaction modal"),
// kept separate from AppState's OnChange (which fires for data mutation/load/sync
// bookkeeping) so the two concerns don't get tangled in an already-large file.
public class UiState
{
    public event Action? RequestAddTransaction;
    public event Action? RequestAddBill;

    public void OpenAddTransaction() => RequestAddTransaction?.Invoke();
    public void OpenAddBill() => RequestAddBill?.Invoke();
}
