namespace Finora.Web.Models;

// Maps a transaction/bill category to a consistent icon + accent colour pair,
// shared between Dashboard and Transactions so the same category always looks
// the same everywhere instead of each page picking its own emoji.
public static class CategoryIcon
{
    public static (string Icon, string Bg, string Fg) Get(string category) => category switch
    {
        "Income" => ("dollar-sign", "rgba(52,211,153,0.15)", "#34D399"),
        "Groceries" => ("cart", "rgba(245,158,11,0.15)", "#F59E0B"),
        "Fuel" => ("droplet", "rgba(96,165,250,0.15)", "#60A5FA"),
        "Rent" => ("home", "rgba(45,212,191,0.15)", "#2DD4BF"),
        "Phone" => ("smartphone", "rgba(167,139,250,0.15)", "#A78BFA"),
        "Mobile Phone" => ("smartphone", "rgba(167,139,250,0.15)", "#A78BFA"),
        "Internet" => ("wifi", "rgba(34,211,238,0.15)", "#22D3EE"),
        "Medical" => ("activity", "rgba(251,113,133,0.15)", "#FB7185"),
        "Health And Medical" => ("heart", "rgba(251,113,133,0.15)", "#FB7185"),
        "Transfer" => ("shuffle", "rgba(148,163,184,0.15)", "#94A3B8"),
        "Opening Balance" => ("shuffle", "rgba(148,163,184,0.15)", "#94A3B8"),
        "Balance Adjustment" => ("shuffle", "rgba(148,163,184,0.15)", "#94A3B8"),
        "Insurance" => ("shield", "rgba(129,140,248,0.15)", "#818CF8"),
        "Car Insurance And Maintenance" => ("shield", "rgba(129,140,248,0.15)", "#818CF8"),
        "Car Loan" => ("credit-card", "rgba(248,113,113,0.15)", "#F87171"),
        "Debt" => ("credit-card", "rgba(248,113,113,0.15)", "#F87171"),
        "Car Repayments" => ("truck", "rgba(248,113,113,0.15)", "#F87171"),
        "Study" => ("book-open", "rgba(251,191,36,0.15)", "#FBBF24"),
        "Education And Student Loans" => ("book-open", "rgba(251,191,36,0.15)", "#FBBF24"),
        "Games And Software" => ("monitor", "rgba(167,139,250,0.15)", "#A78BFA"),
        "Holidays And Travel" => ("send", "rgba(34,211,238,0.15)", "#22D3EE"),
        "Investments" => ("trending-up", "rgba(52,211,153,0.15)", "#34D399"),
        "Life Admin" => ("clipboard", "rgba(148,163,184,0.15)", "#94A3B8"),
        "Misc" => ("question", "rgba(148,163,184,0.15)", "#94A3B8"),
        "Subscription" => ("repeat", "rgba(167,139,250,0.15)", "#A78BFA"),
        "Takeaway" => ("coffee", "rgba(245,158,11,0.15)", "#F59E0B"),
        "Unplanned" => ("alert-circle", "rgba(217,119,6,0.15)", "#D97706"),
        _ => ("dollar-sign", "rgba(45,212,191,0.15)", "#2DD4BF")
    };
}
