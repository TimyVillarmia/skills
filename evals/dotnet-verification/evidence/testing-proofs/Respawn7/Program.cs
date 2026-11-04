using Respawn;
try { await Respawner.CreateAsync("Host=localhost;Database=test_db;Username=postgres;Password=postgres", new RespawnerOptions { SchemasToInclude = ["public"], DbAdapter = DbAdapter.Postgres }); throw new Exception("Expected documented string overload rejection"); }
catch (ArgumentException e) { Console.WriteLine("REPRODUCED: " + e.Message); }
