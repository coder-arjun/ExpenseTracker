# ─────────────────────────────────────────────────────────────────────────
#  secrets.example.ps1  →  copy to "secrets.ps1" (same folder) and fill in.
#  secrets.ps1 is gitignored, so your password never lands in git.
# ─────────────────────────────────────────────────────────────────────────
#
#  Where to get the value:
#    MonsterASP panel → your MSSQL database → "Connection strings"
#    → use the EXTERNAL one (its server is a public address, NOT localhost).
#
#  Notes:
#    • Keep it on ONE line.
#    • If you hit a TLS / certificate error when connecting, append
#        TrustServerCertificate=True;
#    • If you hit an encryption error, also try   Encrypt=False;
#
#  Example shape (yours will have real values):
#    Data Source=db12345.<host>.databaseasp.net;Initial Catalog=db12345;User Id=db12345;Password=YOUR_PASSWORD;TrustServerCertificate=True;

$ProdConnectionString = "Data Source=SERVER;Initial Catalog=DBNAME;User Id=DBUSER;Password=DBPASSWORD;TrustServerCertificate=True;"
