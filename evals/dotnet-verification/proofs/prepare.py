"""Extract audited examples unchanged; add only the documented model-discovery member."""
import json
import re
from pathlib import Path

root = Path(__file__).resolve().parents[3]
out = Path(__file__).resolve().parent
sources = [
    ("references/multi-tenancy.md", [0, 1, 2, 3, 4]),
    ("references/concurrency.md", [1]),
    ("references/api.md", [4]),
]
parts = ["using System.Collections.Concurrent;\nusing System.Linq.Expressions;\n"
         "using Microsoft.EntityFrameworkCore;\nusing Microsoft.EntityFrameworkCore.Diagnostics;\n"
         "using Microsoft.AspNetCore.Diagnostics;\nusing Microsoft.AspNetCore.Mvc;\n"]
manifest = []
for name, indexes in sources:
    path = root / "skills/dotnet" / name
    source = path.read_text()
    blocks = list(re.finditer(r"```csharp\n(.*?)```", source, re.S))
    for index in indexes:
        block = blocks[index]
        code = block.group(1)
        adaptation = "none"
        if "public class AppDbContext(" in code:
            marker = "    protected override void OnModelCreating"
            assert code.count(marker) == 1
            code = code.replace(marker, "    public DbSet<Customer> Customers => Set<Customer>();\n\n" + marker)
            adaptation = "Added DbSet<Customer> Customers for entity discovery; filter code unchanged."
        parts.append(code)
        manifest.append({"file": str(path.relative_to(root)),
                         "line": source[:block.start()].count("\n") + 1,
                         "csharp_index": index, "adaptation": adaptation})
(out / "Snippets.cs").write_text("\n".join(parts))
(out / "snippet-manifest.json").write_text(json.dumps(manifest, indent=2) + "\n")
print(f"Extracted {len(manifest)} snippets")
