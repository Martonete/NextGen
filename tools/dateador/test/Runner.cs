// Runner: ejecuta las dos suites de tests del editor de obj.dat.
// - ObjDatWriter: edicion in situ del texto (nivel bajo)
// - ObjDatabase:  carga, guardado, respaldo y reversion (nivel alto)
int total = 0;
total += AODateador.Test.WriterTests.Run();
Console.WriteLine();
total += AODateador.Test.DatabaseTests.Run();
Console.WriteLine();
Console.WriteLine(total == 0 ? "=== TODO OK ===" : $"=== {total} FALLAS ===");
return total == 0 ? 0 : 1;
