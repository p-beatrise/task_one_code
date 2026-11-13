# task_one_code
Praktiskā darba 1.uzdevuma kods.

Programmas pamatkods ir pieejams *FolderMonitoring* mapē. Datu glabāšanas struktūras ir definētas failā *Models.cs*, bet galvenā programmas loģika ir failā *Program.cs*. Testi ir definēti atsevišķā *FolderMonitoring.Tests* mapē, lai, veidojot programmas izpildes failu, tie netiktu lieki iekļauti tajā. Tie ir atrodami failā *FileRouterTests.cs*.

## Atkarības

Programmas un testu palaišanai ir nepieciešama ierīce ar .NET 10.

## Programmas palaišana

Katalogu monitorēšanas programmu var palaist no *FolderMonitoring* mapes ar komandu: 
*dotnet run --project FolderMonitoring.csproj*.

Programma pieņem vairākus parametrus:
| Arguments | Katalogs | Noklusētais ceļš |
|-----------|----------|-------------|
| `--dirA`  | uzHIS | `<ceļš līdz darbvirsmai>\uzHIS` |
| `--dirB`  | ENDOHIS | `<ceļš līdz darbvirsmai>\ENDOHIS` |
| `--dirC`  | KOLHIS | `<ceļš līdz darbvirsmai>\KOLHIS` |
| `--dirD`  | ENDODM | `<ceļš līdz darbvirsmai>\ENDODM` |
| `--dirE`  | KOLDM | `<ceļš līdz darbvirsmai>\KOLDM` |
| `--logs`  | Audita faili | `<ceļš līdz darbvirsmai>\FolderWatcherLogs` |

Nenorādīto parametru vietā tiks izmantoti noklusētie parametri.

Piemērs komandai ar parametriem:
*dotnet run --project FolderMonitoring.csproj --dirA "<ceļš>\uzHIS" --dirB "<ceļš>\ENDOHIS" --dirC "<ceļš>\KOLHIS" --dirD "<ceļš>\ENDODM" --dirE "<ceļš>\KOLDM" --logs "<ceļš>\FolderWatcherLogs*

Programmu var apstādināt ar Ctrl+C;

## Testu palaišana

Katalogu monitorēšanas programmu var palaist no *FolderMonitoring.Test* mapes ar komandu: 
*dotnet test*.