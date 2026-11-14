// Fichier produit à partir de Donnees/win11-processeurs.txt (même contenu, vérifié par un test automatique).
// Pour actualiser la base intégrée : modifier le .txt, puis recopier son contenu ici à l'identique.
namespace Jvtech.Lecture.Donnees;

public static class BaseWindows11Donnees
{
    public const string Texte = @"# Base de compatibilité Windows 11 : processeurs pris en charge (JVTECH v2, ajustement 4)
# Recopiée des listes officielles de Microsoft (learn.microsoft.com, « Windows 11 supported processors ») :
#   I24 = Intel, Windows 11 24H2, page du 2025-02-27
#   A24 = AMD, Windows 11 24H2, page du 2025-08-08 (EPYC non repris : processeurs de serveur)
#   A25 = AMD, Windows 11 25H2, page du 2025-10-16 (séries récentes seulement)
#   Q24 = Qualcomm, Windows 11 24H2, page du 2025-08-13, et Snapdragon X de la page 25H2
# Les mentions « (OEM Only) », « with Radeon… », « Microsoft Surface Edition » ont été retirées des modèles AMD.
# Microsoft précise que les générations plus récentes qui respectent les mêmes principes sont prises en charge
# même si elles ne sont pas encore dans la liste : un processeur absent n'est donc PAS déclaré incompatible
# s'il n'est pas clairement plus ancien que la liste.
#
# Pour actualiser : copier ce fichier à côté du .exe sous le nom win11-processeurs.txt, changer la date et
# ajouter des lignes. Il n'est utilisé que si sa date est plus récente que celle intégrée au logiciel.
# Format d'une ligne : Fabricant | Libellé officiel | Source | Motif (expression régulière sur le nom lu par Windows)
date=2025-10-16
releve=2026-11-13
Intel | 8th Generation Core i3 Processors | I24 | \bi3-8\d{3}[A-Z0-9]*\b
Intel | 8th Generation Core i5 Processors | I24 | \bi5-8\d{3}[A-Z0-9]*\b
Intel | 8th Generation Core i7 Processors | I24 | \bi7-8\d{3}[A-Z0-9]*\b
Intel | 8th Generation Core i9 Processors | I24 | \bi9-8\d{3}[A-Z0-9]*\b
Intel | 9th Generation Core i3 Processors | I24 | \bi3-9\d{3}[A-Z0-9]*\b
Intel | 9th Generation Core i5 Processors | I24 | \bi5-9\d{3}[A-Z0-9]*\b
Intel | 9th Generation Core i7 Processors | I24 | \bi7-9\d{3}[A-Z0-9]*\b
Intel | 9th Generation Core i9 Processors | I24 | \bi9-9\d{3}[A-Z0-9]*\b
Intel | 10th Generation Core i3 Processors | I24 | \bi3-10\d{2,3}[A-Z0-9]*\b|\b10th Gen Intel\(R\) Core\(TM\) i3\b
Intel | 10th Generation Core i5 Processors | I24 | \bi5-10\d{2,3}[A-Z0-9]*\b|\b10th Gen Intel\(R\) Core\(TM\) i5\b
Intel | 10th Generation Core i7 Processors | I24 | \bi7-10\d{2,3}[A-Z0-9]*\b|\b10th Gen Intel\(R\) Core\(TM\) i7\b
Intel | 10th Generation Core i9 Processors | I24 | \bi9-10\d{2,3}[A-Z0-9]*\b|\b10th Gen Intel\(R\) Core\(TM\) i9\b
Intel | 11th Generation Core i3 Processors | I24 | \bi3-11\d{2,3}[A-Z0-9]*\b|\b11th Gen Intel\(R\) Core\(TM\) i3\b
Intel | 11th Generation Core i5 Processors | I24 | \bi5-11\d{2,3}[A-Z0-9]*\b|\b11th Gen Intel\(R\) Core\(TM\) i5\b
Intel | 11th Generation Core i7 Processors | I24 | \bi7-11\d{2,3}[A-Z0-9]*\b|\b11th Gen Intel\(R\) Core\(TM\) i7\b
Intel | 11th Generation Core i9 Processors | I24 | \bi9-11\d{2,3}[A-Z0-9]*\b|\b11th Gen Intel\(R\) Core\(TM\) i9\b
Intel | 12th Generation Core i3 Processors | I24 | \bi3-12\d{2,3}[A-Z0-9]*\b|\b12th Gen Intel\(R\) Core\(TM\) i3\b
Intel | 12th Generation Core i5 Processors | I24 | \bi5-12\d{2,3}[A-Z0-9]*\b|\b12th Gen Intel\(R\) Core\(TM\) i5\b
Intel | 12th Generation Core i7 Processors | I24 | \bi7-12\d{2,3}[A-Z0-9]*\b|\b12th Gen Intel\(R\) Core\(TM\) i7\b
Intel | 12th Generation Core i9 Processors | I24 | \bi9-12\d{2,3}[A-Z0-9]*\b|\b12th Gen Intel\(R\) Core\(TM\) i9\b
Intel | 13th Generation Core i3 Processors | I24 | \bi3-13\d{2,3}[A-Z0-9]*\b|\b13th Gen Intel\(R\) Core\(TM\) i3\b
Intel | 13th Generation Core i5 Processors | I24 | \bi5-13\d{2,3}[A-Z0-9]*\b|\b13th Gen Intel\(R\) Core\(TM\) i5\b
Intel | 13th Generation Core i7 Processors | I24 | \bi7-13\d{2,3}[A-Z0-9]*\b|\b13th Gen Intel\(R\) Core\(TM\) i7\b
Intel | 13th Generation Core i9 Processors | I24 | \bi9-13\d{2,3}[A-Z0-9]*\b|\b13th Gen Intel\(R\) Core\(TM\) i9\b
Intel | Core i3 processors (14th Generation) | I24 | \bi3-14\d{2,3}[A-Z0-9]*\b|\b14th Gen Intel\(R\) Core\(TM\) i3\b
Intel | Core i5 processors (14th Generation) | I24 | \bi5-14\d{2,3}[A-Z0-9]*\b|\b14th Gen Intel\(R\) Core\(TM\) i5\b
Intel | Core i7 processors (14th Generation) | I24 | \bi7-14\d{2,3}[A-Z0-9]*\b|\b14th Gen Intel\(R\) Core\(TM\) i7\b
Intel | Core i9 processors (14th Generation) | I24 | \bi9-14\d{2,3}[A-Z0-9]*\b|\b14th Gen Intel\(R\) Core\(TM\) i9\b
Intel | 8th Generation Core m Processors | I24 | \bm3-8\d{3}[A-Z]*\b|Core\(TM\) m3-8\d{3}
Intel | Core Processor N300 Series | I24 | \bi3-N3\d{2}\b
Intel | Core Processors (Series 1) | I24 | Intel\(R\) Core\(TM\) [3579] 1\d{2}[A-Z]{1,2}\b
Intel | Core Processors (Series 2) | I24 | Intel\(R\) Core\(TM\) [3579] 2\d{2}[A-Z]{1,2}\b
Intel | Core Ultra Processors (Series 1) | I24 | Core\(TM\) Ultra [3579] 1\d{2}[A-Z]{0,2}\b|Core Ultra [3579] 1\d{2}[A-Z]{0,2}\b
Intel | Core Ultra Processors (Series 2) | I24 | Core\(TM\) Ultra [3579] 2\d{2}[A-Z]{0,2}\b|Core Ultra [3579] 2\d{2}[A-Z]{0,2}\b
Intel | Core Ultra Processors (Series 3) | I24 | Core\(TM\) Ultra [3579] 3\d{2}[A-Z]{0,2}\b|Core Ultra [3579] 3\d{2}[A-Z]{0,2}\b
Intel | Atom X7000 Series | I24 | Atom\(TM\) x7\d{3}[A-Z]*\b|Atom.*\bx7\d{3}
Intel | Celeron 3000 Series | I24 | Celeron\(R\)( CPU| Processor)? 3\d{3}[A-Z]?\b
Intel | Celeron 4000 Series | I24 | Celeron\(R\)( CPU| Processor)? 4\d{3}[A-Z]?\b
Intel | Celeron 5000 Series | I24 | Celeron\(R\)( CPU| Processor)? 5\d{3}[A-Z]?\b
Intel | Celeron 6000 Series | I24 | Celeron\(R\)( CPU| Processor)? 6\d{3}[A-Z]?\b
Intel | Celeron 7000 Series | I24 | Celeron\(R\)( CPU| Processor)? 7\d{3}[A-Z]?\b
Intel | Celeron G4000 Series | I24 | Celeron\(R\)( CPU| Processor)? G4\d{3}[A-Z]?\b
Intel | Celeron G5000 Series | I24 | Celeron\(R\)( CPU| Processor)? G5\d{3}[A-Z]?\b
Intel | Celeron G6000 Series | I24 | Celeron\(R\)( CPU| Processor)? G6\d{3}[A-Z]?\b
Intel | Celeron J4000 Series | I24 | Celeron\(R\)( CPU| Processor)? J4\d{3}\b
Intel | Celeron N4000 Series | I24 | Celeron\(R\)( CPU| Processor)? N4\d{3}\b
Intel | Celeron N5000 Series | I24 | Celeron\(R\)( CPU| Processor)? N5\d{3}\b
Intel | Pentium 6800 Series | I24 | Pentium\(R\).*\b68\d{2}[A-Z]?\b
Intel | Pentium Gold 4000U Series | I24 | Pentium\(R\).*Gold( CPU)? 4\d{3}U\b
Intel | Pentium Gold 4000Y Series | I24 | Pentium\(R\).*Gold( CPU)? 4\d{3}Y\b
Intel | Pentium Gold 5000 Series | I24 | Pentium\(R\).*Gold( CPU)? 5\d{3}[A-Z]?\b
Intel | Pentium Gold 6000 Series | I24 | Pentium\(R\).*Gold( CPU)? 6\d{3}[A-Z]?\b
Intel | Pentium Gold 6000Y Series | I24 | Pentium\(R\).*Gold( CPU)? 6\d{3}Y\b
Intel | Pentium Gold 7000 Series | I24 | Pentium\(R\).*Gold( CPU)? 7\d{3}[A-Z]?\b
Intel | Pentium Gold 8000 Series | I24 | Pentium\(R\).*Gold( CPU)? 8\d{3}[A-Z]?\b
Intel | Pentium Gold G5000 Series | I24 | Pentium\(R\).*Gold( CPU)? G5\d{3}[A-Z]?\b
Intel | Pentium Gold G7000 Series | I24 | Pentium\(R\).*Gold( CPU)? G7\d{3}[A-Z]?\b
Intel | Pentium Silver J5000 Series | I24 | Pentium\(R\).*Silver( CPU)? J5\d{3}\b
Intel | Pentium Silver N6000 Series | I24 | Pentium\(R\).*Silver( CPU)? N6\d{3}\b
Intel | Intel® 300 Processor for Desktop | I24 | Intel\(R\) Processor 300T?\b
Intel | N100 Series | I24 | Intel\(R\) N1\d{2}\b|\bProcessor N1\d{2}\b
Intel | N200 Series | I24 | Intel\(R\) N2\d{2}\b|\bProcessor N2\d{2}\b
Intel | N90 Series | I24 | Intel\(R\) N9\d\b|\bProcessor N9\d\b
Intel | U300 series | I24 | Intel\(R\) Processor U300E?\b|\bU300E?\b
Intel | Xeon W-2400 Series | I24 | Xeon\(R\).*\bW-24\d{2}[A-Z]*\b
Intel | Xeon W-2500 Series | I24 | Xeon\(R\).*\bW-25\d{2}[A-Z]*\b
Intel | Xeon W-3400 Series | I24 | Xeon\(R\).*\bW-34\d{2}[A-Z]*\b
Intel | Xeon W-3500 Series | I24 | Xeon\(R\).*\bW-35\d{2}[A-Z]*\b
Intel | Xeon D-1700 Series | I24 | Xeon\(R\).*\bD-17\d{2}[A-Z]*\b
Intel | Xeon D-1800 Series | I24 | Xeon\(R\).*\bD-18\d{2}[A-Z]*\b
Intel | Xeon D-2700 Series | I24 | Xeon\(R\).*\bD-27\d{2}[A-Z]*\b
Intel | Xeon D-2800 Series | I24 | Xeon\(R\).*\bD-28\d{2}[A-Z]*\b
Intel | Xeon E-2100 Series | I24 | Xeon\(R\).*\bE-21\d{2}[A-Z]*\b
Intel | Xeon E-2200 Series | I24 | Xeon\(R\).*\bE-22\d{2}[A-Z]*\b
Intel | Xeon E-2300 Series | I24 | Xeon\(R\).*\bE-23\d{2}[A-Z]*\b
Intel | Xeon E-2400 Series | I24 | Xeon\(R\).*\bE-24\d{2}[A-Z]*\b
Intel | Xeon W-10000M Series | I24 | Xeon\(R\).*\bW-108\d{2}M[A-Z]*\b
Intel | Xeon W-11000M Series | I24 | Xeon\(R\).*\bW-11\d{3}M[A-Z]*\b
Intel | Xeon W-1200 Series | I24 | Xeon\(R\).*\bW-12\d{2}[A-Z]*\b
Intel | Xeon W-1300 Series | I24 | Xeon\(R\).*\bW-13\d{2}[A-Z]*\b
Intel | Xeon W-3300 Series | I24 | Xeon\(R\).*\bW-33\d{2}[A-Z]*\b
Intel | Xeon Scalable Processors | I24 | Xeon\(R\) (Bronze|Silver|Gold|Platinum) [3-9]1\d{2}[A-Z]*\b
Intel | 2nd Generation Xeon Scalable Processors | I24 | Xeon\(R\) (Bronze|Silver|Gold|Platinum) [3-9]2\d{2}[A-Z]*\b
Intel | 3rd Generation Xeon Scalable Processors | I24 | Xeon\(R\) (Bronze|Silver|Gold|Platinum) [3-9]3\d{2}[A-Z]*\b
Intel | 4th Generation Xeon Scalable Processors | I24 | Xeon\(R\) (Bronze|Silver|Gold|Platinum) [3-9]4\d{2}[A-Z]*\b
Intel | 5th Generation Xeon Scalable Processors | I24 | Xeon\(R\) (Bronze|Silver|Gold|Platinum) [3-9]5\d{2}[A-Z]*\b
Intel | Xeon 600 Processors for Workstation | I24 | Xeon\(R\) 6\d{3}[A-Z]*\b
AMD | AMD 3015e | A24 | AMD \b3015e\b
AMD | AMD 3020e | A24 | AMD \b3020e\b
AMD | Athlon 3000G | A24 | Athlon.*\b3000G\b
AMD | Athlon 300GE | A24 | Athlon.*\b300GE\b
AMD | Athlon 300U | A24 | Athlon.*\b300U\b
AMD | Athlon 320GE | A24 | Athlon.*\b320GE\b
AMD | Athlon 7120e | A24 | Athlon.*\b7120e\b
AMD | Athlon 7120U | A24 | Athlon.*\b7120U\b
AMD | Athlon 7220e | A24 | Athlon.*\b7220e\b
AMD | Athlon 7220U | A24 | Athlon.*\b7220U\b
AMD | Athlon Gold 3150C | A24 | Athlon.*\bGold\ 3150C\b
AMD | Athlon Gold 3150G | A24 | Athlon.*\bGold\ 3150G\b
AMD | Athlon Gold 3150GE | A24 | Athlon.*\bGold\ 3150GE\b
AMD | Athlon Gold 3150U | A24 | Athlon.*\bGold\ 3150U\b
AMD | Athlon Gold PRO 3125GE | A24 | Athlon.*\bGold\ PRO\ 3125GE\b
AMD | Athlon Gold PRO 3150G | A24 | Athlon.*\bGold\ PRO\ 3150G\b
AMD | Athlon Gold PRO 3150GE | A24 | Athlon.*\bGold\ PRO\ 3150GE\b
AMD | Athlon Gold PRO 4150GE | A24 | Athlon.*\bGold\ PRO\ 4150GE\b
AMD | Athlon PRO 300GE | A24 | Athlon.*\bPRO\ 300GE\b
AMD | Athlon PRO 300U | A24 | Athlon.*\bPRO\ 300U\b
AMD | Athlon PRO 3045B | A24 | Athlon.*\bPRO\ 3045B\b
AMD | Athlon Silver 3050C | A24 | Athlon.*\bSilver\ 3050C\b
AMD | Athlon Silver 3050e | A24 | Athlon.*\bSilver\ 3050e\b
AMD | Athlon Silver 3050GE | A24 | Athlon.*\bSilver\ 3050GE\b
AMD | Athlon Silver 3050U | A24 | Athlon.*\bSilver\ 3050U\b
AMD | Ryzen Embedded R2000 Series R2312 | A24 | Ryzen.*Embedded.*|Embedded.*\bR2312\b
AMD | Ryzen Embedded R2000 Series R2314 | A24 | Ryzen.*Embedded.*|Embedded.*\bR2314\b
AMD | Ryzen Embedded R2000 Series R2514 | A24 | Ryzen.*Embedded.*|Embedded.*\bR2514\b
AMD | Ryzen Embedded R2000 Series R2544 | A24 | Ryzen.*Embedded.*|Embedded.*\bR2544\b
AMD | Ryzen 3 PRO 3200G | A24 | Ryzen.*\bPRO\b.*\b3200G\b
AMD | Ryzen 3 PRO 3200GE | A24 | Ryzen.*\bPRO\b.*\b3200GE\b
AMD | Ryzen 3 PRO 3300U | A24 | Ryzen.*\bPRO\b.*\b3300U\b
AMD | Ryzen 3 PRO 4350G | A24 | Ryzen.*\bPRO\b.*\b4350G\b
AMD | Ryzen 3 PRO 4350GE | A24 | Ryzen.*\bPRO\b.*\b4350GE\b
AMD | Ryzen 3 PRO 4355G | A24 | Ryzen.*\bPRO\b.*\b4355G\b
AMD | Ryzen 3 PRO 4355GE | A24 | Ryzen.*\bPRO\b.*\b4355GE\b
AMD | Ryzen 3 PRO 4450U | A24 | Ryzen.*\bPRO\b.*\b4450U\b
AMD | Ryzen 3 PRO 5350G | A24 | Ryzen.*\bPRO\b.*\b5350G\b
AMD | Ryzen 3 PRO 5350GE | A24 | Ryzen.*\bPRO\b.*\b5350GE\b
AMD | Ryzen 3 PRO 5450U | A24 | Ryzen.*\bPRO\b.*\b5450U\b
AMD | Ryzen 3 PRO 5475U | A24 | Ryzen.*\bPRO\b.*\b5475U\b
AMD | Ryzen 3 PRO 7330U | A24 | Ryzen.*\bPRO\b.*\b7330U\b
AMD | Ryzen 3 PRO 8300G | A24 | Ryzen.*\bPRO\b.*\b8300G\b
AMD | Ryzen 3 PRO 8300GE | A24 | Ryzen.*\bPRO\b.*\b8300GE\b
AMD | Ryzen 3 2300X | A24 | Ryzen.*\b2300X\b
AMD | Ryzen 3 3100 | A24 | Ryzen.*\b3100\b
AMD | Ryzen 3 3200G | A24 | Ryzen.*\b3200G\b
AMD | Ryzen 3 3200GE | A24 | Ryzen.*\b3200GE\b
AMD | Ryzen 3 3200U | A24 | Ryzen.*\b3200U\b
AMD | Ryzen 3 3250C | A24 | Ryzen.*\b3250C\b
AMD | Ryzen 3 3250U | A24 | Ryzen.*\b3250U\b
AMD | Ryzen 3 3300U | A24 | Ryzen.*\b3300U\b
AMD | Ryzen 3 3350U | A24 | Ryzen.*\b3350U\b
AMD | Ryzen 3 4100 | A24 | Ryzen.*\b4100\b
AMD | Ryzen 3 4300G | A24 | Ryzen.*\b4300G\b
AMD | Ryzen 3 4300GE | A24 | Ryzen.*\b4300GE\b
AMD | Ryzen 3 4300U | A24 | Ryzen.*\b4300U\b
AMD | Ryzen 3 5125C | A24 | Ryzen.*\b5125C\b
AMD | Ryzen 3 5300G | A24 | Ryzen.*\b5300G\b
AMD | Ryzen 3 5300GE | A24 | Ryzen.*\b5300GE\b
AMD | Ryzen 3 5300U | A24 | Ryzen.*\b5300U\b
AMD | Ryzen 3 5380U | A24 | Ryzen.*\b5380U\b
AMD | Ryzen 3 5400U | A24 | Ryzen.*\b5400U\b
AMD | Ryzen 3 5425C | A24 | Ryzen.*\b5425C\b
AMD | Ryzen 3 5425U | A24 | Ryzen.*\b5425U\b
AMD | Ryzen 3 7320e | A24 | Ryzen.*\b7320e\b
AMD | Ryzen 3 7320U | A24 | Ryzen.*\b7320U\b
AMD | Ryzen 3 7330U | A24 | Ryzen.*\b7330U\b
AMD | Ryzen 3 7335U | A24 | Ryzen.*\b7335U\b
AMD | Ryzen 3 7440U | A24 | Ryzen.*\b7440U\b
AMD | Ryzen 3 8300G | A24 | Ryzen.*\b8300G\b
AMD | Ryzen 5 PRO 2600 | A24 | Ryzen.*\bPRO\b.*\b2600\b
AMD | Ryzen 5 PRO 3350G | A24 | Ryzen.*\bPRO\b.*\b3350G\b
AMD | Ryzen 5 PRO 3350GE | A24 | Ryzen.*\bPRO\b.*\b3350GE\b
AMD | Ryzen 5 PRO 3400G | A24 | Ryzen.*\bPRO\b.*\b3400G\b
AMD | Ryzen 5 PRO 3400GE | A24 | Ryzen.*\bPRO\b.*\b3400GE\b
AMD | Ryzen 5 PRO 3500U | A24 | Ryzen.*\bPRO\b.*\b3500U\b
AMD | Ryzen 5 PRO 3600 | A24 | Ryzen.*\bPRO\b.*\b3600\b
AMD | Ryzen 5 PRO 4650G | A24 | Ryzen.*\bPRO\b.*\b4650G\b
AMD | Ryzen 5 PRO 4650GE | A24 | Ryzen.*\bPRO\b.*\b4650GE\b
AMD | Ryzen 5 PRO 4650U | A24 | Ryzen.*\bPRO\b.*\b4650U\b
AMD | Ryzen 5 PRO 4655G | A24 | Ryzen.*\bPRO\b.*\b4655G\b
AMD | Ryzen 5 PRO 4655GE | A24 | Ryzen.*\bPRO\b.*\b4655GE\b
AMD | Ryzen 5 PRO 5475U | A24 | Ryzen.*\bPRO\b.*\b5475U\b
AMD | Ryzen 5 PRO 5645 | A24 | Ryzen.*\bPRO\b.*\b5645\b
AMD | Ryzen 5 PRO 5650G | A24 | Ryzen.*\bPRO\b.*\b5650G\b
AMD | Ryzen 5 PRO 5650GE | A24 | Ryzen.*\bPRO\b.*\b5650GE\b
AMD | Ryzen 5 PRO 5650HS | A24 | Ryzen.*\bPRO\b.*\b5650HS\b
AMD | Ryzen 5 PRO 5650HX | A24 | Ryzen.*\bPRO\b.*\b5650HX\b
AMD | Ryzen 5 PRO 5650U | A24 | Ryzen.*\bPRO\b.*\b5650U\b
AMD | Ryzen 5 PRO 5675U | A24 | Ryzen.*\bPRO\b.*\b5675U\b
AMD | Ryzen 5 PRO 5750G | A24 | Ryzen.*\bPRO\b.*\b5750G\b
AMD | Ryzen 5 PRO 5750GE | A24 | Ryzen.*\bPRO\b.*\b5750GE\b
AMD | Ryzen 5 PRO 6650H | A24 | Ryzen.*\bPRO\b.*\b6650H\b
AMD | Ryzen 5 PRO 6650HS | A24 | Ryzen.*\bPRO\b.*\b6650HS\b
AMD | Ryzen 5 PRO 6650U | A24 | Ryzen.*\bPRO\b.*\b6650U\b
AMD | Ryzen 5 PRO 7530U | A24 | Ryzen.*\bPRO\b.*\b7530U\b
AMD | Ryzen 5 PRO 7535U | A24 | Ryzen.*\bPRO\b.*\b7535U\b
AMD | Ryzen 5 PRO 7540U | A24 | Ryzen.*\bPRO\b.*\b7540U\b
AMD | Ryzen 5 PRO 7640U | A24 | Ryzen.*\bPRO\b.*\b7640U\b
AMD | Ryzen 5 PRO 8500G | A24 | Ryzen.*\bPRO\b.*\b8500G\b
AMD | Ryzen 5 PRO 8500GE | A24 | Ryzen.*\bPRO\b.*\b8500GE\b
AMD | Ryzen 5 PRO 8540U | A24 | Ryzen.*\bPRO\b.*\b8540U\b
AMD | Ryzen 5 PRO 8600G | A24 | Ryzen.*\bPRO\b.*\b8600G\b
AMD | Ryzen 5 PRO 8600GE | A24 | Ryzen.*\bPRO\b.*\b8600GE\b
AMD | Ryzen 5 PRO 8640HS | A24 | Ryzen.*\bPRO\b.*\b8640HS\b
AMD | Ryzen 5 PRO 8640U | A24 | Ryzen.*\bPRO\b.*\b8640U\b
AMD | Ryzen 5 PRO 8645HS | A24 | Ryzen.*\bPRO\b.*\b8645HS\b
AMD | Ryzen 5 2500X | A24 | Ryzen.*\b2500X\b
AMD | Ryzen 5 2600 | A24 | Ryzen.*\b2600\b
AMD | Ryzen 5 2600E | A24 | Ryzen.*\b2600E\b
AMD | Ryzen 5 2600X | A24 | Ryzen.*\b2600X\b
AMD | Ryzen 5 3350G | A24 | Ryzen.*\b3350G\b
AMD | Ryzen 5 3350GE | A24 | Ryzen.*\b3350GE\b
AMD | Ryzen 5 3400G | A24 | Ryzen.*\b3400G\b
AMD | Ryzen 5 3400GE | A24 | Ryzen.*\b3400GE\b
AMD | Ryzen 5 3450U | A24 | Ryzen.*\b3450U\b
AMD | Ryzen 5 3500 | A24 | Ryzen.*\b3500\b
AMD | Ryzen 5 3500C | A24 | Ryzen.*\b3500C\b
AMD | Ryzen 5 3500U | A24 | Ryzen.*\b3500U\b
AMD | Ryzen 5 3500X | A24 | Ryzen.*\b3500X\b
AMD | Ryzen 5 3550H | A24 | Ryzen.*\b3550H\b
AMD | Ryzen 5 3580U | A24 | Ryzen.*\b3580U\b
AMD | Ryzen 5 3600 | A24 | Ryzen.*\b3600\b
AMD | Ryzen 5 3600X | A24 | Ryzen.*\b3600X\b
AMD | Ryzen 5 3600XT | A24 | Ryzen.*\b3600XT\b
AMD | Ryzen 5 4500 | A24 | Ryzen.*\b4500\b
AMD | Ryzen 5 4500U | A24 | Ryzen.*\b4500U\b
AMD | Ryzen 5 4600G | A24 | Ryzen.*\b4600G\b
AMD | Ryzen 5 4600GE | A24 | Ryzen.*\b4600GE\b
AMD | Ryzen 5 4600H | A24 | Ryzen.*\b4600H\b
AMD | Ryzen 5 4600HS | A24 | Ryzen.*\b4600HS\b
AMD | Ryzen 5 4600U | A24 | Ryzen.*\b4600U\b
AMD | Ryzen 5 5300G | A24 | Ryzen.*\b5300G\b
AMD | Ryzen 5 5300GE | A24 | Ryzen.*\b5300GE\b
AMD | Ryzen 5 5425U | A24 | Ryzen.*\b5425U\b
AMD | Ryzen 5 5500 | A24 | Ryzen.*\b5500\b
AMD | Ryzen 5 5500U | A24 | Ryzen.*\b5500U\b
AMD | Ryzen 5 5560U | A24 | Ryzen.*\b5560U\b
AMD | Ryzen 5 5600 | A24 | Ryzen.*\b5600\b
AMD | Ryzen 5 5600G | A24 | Ryzen.*\b5600G\b
AMD | Ryzen 5 5600GE | A24 | Ryzen.*\b5600GE\b
AMD | Ryzen 5 5600H | A24 | Ryzen.*\b5600H\b
AMD | Ryzen 5 5600HS | A24 | Ryzen.*\b5600HS\b
AMD | Ryzen 5 5600U | A24 | Ryzen.*\b5600U\b
AMD | Ryzen 5 5600X | A24 | Ryzen.*\b5600X\b
AMD | Ryzen 5 5625C | A24 | Ryzen.*\b5625C\b
AMD | Ryzen 5 5625U | A24 | Ryzen.*\b5625U\b
AMD | Ryzen 5 6600H | A24 | Ryzen.*\b6600H\b
AMD | Ryzen 5 6600HS | A24 | Ryzen.*\b6600HS\b
AMD | Ryzen 5 6600U | A24 | Ryzen.*\b6600U\b
AMD | Ryzen 5 7430U | A24 | Ryzen.*\b7430U\b
AMD | Ryzen 5 7520U | A24 | Ryzen.*\b7520U\b
AMD | Ryzen 5 7530U | A24 | Ryzen.*\b7530U\b
AMD | Ryzen 5 7535HS | A24 | Ryzen.*\b7535HS\b
AMD | Ryzen 5 7535U | A24 | Ryzen.*\b7535U\b
AMD | Ryzen 5 7540U | A24 | Ryzen.*\b7540U\b
AMD | Ryzen 5 7600 | A24 | Ryzen.*\b7600\b
AMD | Ryzen 5 7600X | A24 | Ryzen.*\b7600X\b
AMD | Ryzen 5 7640H | A24 | Ryzen.*\b7640H\b
AMD | Ryzen 5 7640HS | A24 | Ryzen.*\b7640HS\b
AMD | Ryzen 5 7640S | A24 | Ryzen.*\b7640S\b
AMD | Ryzen 5 7640U | A24 | Ryzen.*\b7640U\b
AMD | Ryzen 5 7645HX | A24 | Ryzen.*\b7645HX\b
AMD | Ryzen 5 8500G | A24 | Ryzen.*\b8500G\b
AMD | Ryzen 5 8600G | A24 | Ryzen.*\b8600G\b
AMD | Ryzen 7 PRO 2700 | A24 | Ryzen.*\bPRO\b.*\b2700\b
AMD | Ryzen 7 PRO 2700X | A24 | Ryzen.*\bPRO\b.*\b2700X\b
AMD | Ryzen 7 PRO 3700 | A24 | Ryzen.*\bPRO\b.*\b3700\b
AMD | Ryzen 7 PRO 3700U | A24 | Ryzen.*\bPRO\b.*\b3700U\b
AMD | Ryzen 7 PRO 4750G | A24 | Ryzen.*\bPRO\b.*\b4750G\b
AMD | Ryzen 7 PRO 4750GE | A24 | Ryzen.*\bPRO\b.*\b4750GE\b
AMD | Ryzen 7 PRO 4750U | A24 | Ryzen.*\bPRO\b.*\b4750U\b
AMD | Ryzen 7 PRO 5845 | A24 | Ryzen.*\bPRO\b.*\b5845\b
AMD | Ryzen 7 PRO 5850HS | A24 | Ryzen.*\bPRO\b.*\b5850HS\b
AMD | Ryzen 7 PRO 5850HX | A24 | Ryzen.*\bPRO\b.*\b5850HX\b
AMD | Ryzen 7 PRO 5850U | A24 | Ryzen.*\bPRO\b.*\b5850U\b
AMD | Ryzen 7 PRO 5875U | A24 | Ryzen.*\bPRO\b.*\b5875U\b
AMD | Ryzen 7 PRO 6850H | A24 | Ryzen.*\bPRO\b.*\b6850H\b
AMD | Ryzen 7 PRO 6850HS | A24 | Ryzen.*\bPRO\b.*\b6850HS\b
AMD | Ryzen 7 PRO 6850U | A24 | Ryzen.*\bPRO\b.*\b6850U\b
AMD | Ryzen 7 PRO 6860Z | A24 | Ryzen.*\bPRO\b.*\b6860Z\b
AMD | Ryzen 7 PRO 7730U | A24 | Ryzen.*\bPRO\b.*\b7730U\b
AMD | Ryzen 7 PRO 7840HS | A24 | Ryzen.*\bPRO\b.*\b7840HS\b
AMD | Ryzen 7 PRO 7840U | A24 | Ryzen.*\bPRO\b.*\b7840U\b
AMD | Ryzen 7 PRO 8700G | A24 | Ryzen.*\bPRO\b.*\b8700G\b
AMD | Ryzen 7 PRO 8840HS | A24 | Ryzen.*\bPRO\b.*\b8840HS\b
AMD | Ryzen 7 PRO 8840U | A24 | Ryzen.*\bPRO\b.*\b8840U\b
AMD | Ryzen 7 PRO 8845HS | A24 | Ryzen.*\bPRO\b.*\b8845HS\b
AMD | Ryzen 7 PRO 8945HS | A24 | Ryzen.*\bPRO\b.*\b8945HS\b
AMD | Ryzen 7 2700 | A24 | Ryzen.*\b2700\b
AMD | Ryzen 7 2700E | A24 | Ryzen.*\b2700E\b
AMD | Ryzen 7 2700X | A24 | Ryzen.*\b2700X\b
AMD | Ryzen 7 3700C | A24 | Ryzen.*\b3700C\b
AMD | Ryzen 7 3700U | A24 | Ryzen.*\b3700U\b
AMD | Ryzen 7 3700X | A24 | Ryzen.*\b3700X\b
AMD | Ryzen 7 3750H | A24 | Ryzen.*\b3750H\b
AMD | Ryzen 7 3780U | A24 | Ryzen.*\b3780U\b
AMD | Ryzen 7 3800X | A24 | Ryzen.*\b3800X\b
AMD | Ryzen 7 3800XT | A24 | Ryzen.*\b3800XT\b
AMD | Ryzen 7 4700G | A24 | Ryzen.*\b4700G\b
AMD | Ryzen 7 4700GE | A24 | Ryzen.*\b4700GE\b
AMD | Ryzen 7 4700U | A24 | Ryzen.*\b4700U\b
AMD | Ryzen 7 4800H | A24 | Ryzen.*\b4800H\b
AMD | Ryzen 7 4800HS | A24 | Ryzen.*\b4800HS\b
AMD | Ryzen 7 4800U | A24 | Ryzen.*\b4800U\b
AMD | Ryzen 7 5700G | A24 | Ryzen.*\b5700G\b
AMD | Ryzen 7 5700GE | A24 | Ryzen.*\b5700GE\b
AMD | Ryzen 7 5700U | A24 | Ryzen.*\b5700U\b
AMD | Ryzen 7 5700X | A24 | Ryzen.*\b5700X\b
AMD | Ryzen 7 5800 | A24 | Ryzen.*\b5800\b
AMD | Ryzen 7 5800H | A24 | Ryzen.*\b5800H\b
AMD | Ryzen 7 5800HS | A24 | Ryzen.*\b5800HS\b
AMD | Ryzen 7 5800U | A24 | Ryzen.*\b5800U\b
AMD | Ryzen 7 5800X | A24 | Ryzen.*\b5800X\b
AMD | Ryzen 7 5800X3D | A24 | Ryzen.*\b5800X3D\b
AMD | Ryzen 7 5825C | A24 | Ryzen.*\b5825C\b
AMD | Ryzen 7 5825U | A24 | Ryzen.*\b5825U\b
AMD | Ryzen 7 6800H | A24 | Ryzen.*\b6800H\b
AMD | Ryzen 7 6800HS | A24 | Ryzen.*\b6800HS\b
AMD | Ryzen 7 6800U | A24 | Ryzen.*\b6800U\b
AMD | Ryzen 7 6810U | A24 | Ryzen.*\b6810U\b
AMD | Ryzen 7 7700 | A24 | Ryzen.*\b7700\b
AMD | Ryzen 7 7700X | A24 | Ryzen.*\b7700X\b
AMD | Ryzen 7 7730U | A24 | Ryzen.*\b7730U\b
AMD | Ryzen 7 7735HS | A24 | Ryzen.*\b7735HS\b
AMD | Ryzen 7 7735U | A24 | Ryzen.*\b7735U\b
AMD | Ryzen 7 7736U | A24 | Ryzen.*\b7736U\b
AMD | Ryzen 7 7745HX | A24 | Ryzen.*\b7745HX\b
AMD | Ryzen 7 7800X3D | A24 | Ryzen.*\b7800X3D\b
AMD | Ryzen 7 7840H | A24 | Ryzen.*\b7840H\b
AMD | Ryzen 7 7840HS | A24 | Ryzen.*\b7840HS\b
AMD | Ryzen 7 7840S | A24 | Ryzen.*\b7840S\b
AMD | Ryzen 7 7840U | A24 | Ryzen.*\b7840U\b
AMD | Ryzen 7 8700G | A24 | Ryzen.*\b8700G\b
AMD | Ryzen 9 PRO 3900 | A24 | Ryzen.*\bPRO\b.*\b3900\b
AMD | Ryzen 9 PRO 5945 | A24 | Ryzen.*\bPRO\b.*\b5945\b
AMD | Ryzen 9 PRO 6950H | A24 | Ryzen.*\bPRO\b.*\b6950H\b
AMD | Ryzen 9 PRO 6950HS | A24 | Ryzen.*\bPRO\b.*\b6950HS\b
AMD | Ryzen 9 3900 | A24 | Ryzen.*\b3900\b
AMD | Ryzen 9 3900X | A24 | Ryzen.*\b3900X\b
AMD | Ryzen 9 3900XT | A24 | Ryzen.*\b3900XT\b
AMD | Ryzen 9 3950X | A24 | Ryzen.*\b3950X\b
AMD | Ryzen 9 4900H | A24 | Ryzen.*\b4900H\b
AMD | Ryzen 9 4900HS | A24 | Ryzen.*\b4900HS\b
AMD | Ryzen 9 5900 | A24 | Ryzen.*\b5900\b
AMD | Ryzen 9 5900HS | A24 | Ryzen.*\b5900HS\b
AMD | Ryzen 9 5900HX | A24 | Ryzen.*\b5900HX\b
AMD | Ryzen 9 5900X | A24 | Ryzen.*\b5900X\b
AMD | Ryzen 9 5950X | A24 | Ryzen.*\b5950X\b
AMD | Ryzen 9 5980HS | A24 | Ryzen.*\b5980HS\b
AMD | Ryzen 9 5980HX | A24 | Ryzen.*\b5980HX\b
AMD | Ryzen 9 6900HS | A24 | Ryzen.*\b6900HS\b
AMD | Ryzen 9 6900HX | A24 | Ryzen.*\b6900HX\b
AMD | Ryzen 9 6980HS | A24 | Ryzen.*\b6980HS\b
AMD | Ryzen 9 6980HX | A24 | Ryzen.*\b6980HX\b
AMD | Ryzen 9 7845HX | A24 | Ryzen.*\b7845HX\b
AMD | Ryzen 9 7900 | A24 | Ryzen.*\b7900\b
AMD | Ryzen 9 7900X | A24 | Ryzen.*\b7900X\b
AMD | Ryzen 9 7900X3D | A24 | Ryzen.*\b7900X3D\b
AMD | Ryzen 9 7940H | A24 | Ryzen.*\b7940H\b
AMD | Ryzen 9 7940HS | A24 | Ryzen.*\b7940HS\b
AMD | Ryzen 9 7945HX | A24 | Ryzen.*\b7945HX\b
AMD | Ryzen 9 7950X | A24 | Ryzen.*\b7950X\b
AMD | Ryzen 9 7950X3D | A24 | Ryzen.*\b7950X3D\b
AMD | Ryzen Embedded V2516 | A24 | Ryzen.*Embedded.*|Embedded.*\bV2516\b
AMD | Ryzen Embedded V2546 | A24 | Ryzen.*Embedded.*|Embedded.*\bV2546\b
AMD | Ryzen Embedded V2718 | A24 | Ryzen.*Embedded.*|Embedded.*\bV2718\b
AMD | Ryzen Embedded V2748 | A24 | Ryzen.*Embedded.*|Embedded.*\bV2748\b
AMD | Ryzen Threadripper PRO 3945WX | A24 | Threadripper.*\b3945WX\b
AMD | Ryzen Threadripper PRO 3955WX | A24 | Threadripper.*\b3955WX\b
AMD | Ryzen Threadripper PRO 3975WX | A24 | Threadripper.*\b3975WX\b
AMD | Ryzen Threadripper PRO 3995WX | A24 | Threadripper.*\b3995WX\b
AMD | Ryzen Threadripper PRO 5945WX | A24 | Threadripper.*\b5945WX\b
AMD | Ryzen Threadripper PRO 5955WX | A24 | Threadripper.*\b5955WX\b
AMD | Ryzen Threadripper PRO 5965WX | A24 | Threadripper.*\b5965WX\b
AMD | Ryzen Threadripper PRO 5975WX | A24 | Threadripper.*\b5975WX\b
AMD | Ryzen Threadripper PRO 5995WX | A24 | Threadripper.*\b5995WX\b
AMD | Ryzen Z1 Extreme | A24 | Ryzen.*\bZ1\ Extreme\b
AMD | Ryzen Z1 | A24 | Ryzen.*\bZ1\b
AMD | Athlon 7000 U Series | A25 | Athlon.*\b7\d{3}U\b
AMD | Ryzen 7000 Series | A25 | Ryzen [3579] (PRO )?7\d{3}[A-Z0-9]*\b
AMD | Ryzen 8000 Series | A25 | Ryzen [3579] (PRO )?8\d{3}[A-Z0-9]*\b
AMD | Ryzen 9000 Series | A25 | Ryzen [3579] (PRO )?9\d{3}[A-Z0-9]*\b
AMD | Ryzen 200 Series | A25 | Ryzen [3579] (PRO )?2\d{2}\b
AMD | Ryzen AI 300 Series | A25 | Ryzen AI [579] (PRO )?(HX )?3\d{2}\b
AMD | Ryzen AI Max 300 Series | A25 | Ryzen AI Max\+? (PRO )?3\d{2}\b
AMD | Ryzen AI Z2 Extreme | A25 | Ryzen AI Z2 Extreme
AMD | Ryzen Z1 Series | A25 | Ryzen Z1\b
AMD | Ryzen Z2 Series | A25 | Ryzen Z2\b
AMD | Ryzen Threadripper 7000 / 9000 Series | A25 | Threadripper (PRO )?[79]\d{3}[A-Z]*\b
Qualcomm | Snapdragon 7c+ Gen 3 | Q24 | Snapdragon.*7c\+ Gen 3
Qualcomm | Snapdragon 8cx Gen 2 | Q24 | Snapdragon.*8cx Gen 2
Qualcomm | Snapdragon 8cx Gen 3 | Q24 | Snapdragon.*8cx Gen 3
Qualcomm | QCM6490 / QCS6490 | Q24 | QC[MS]6490
Qualcomm | QCM5430 / QCS5430 | Q24 | QC[MS]5430
Qualcomm | Microsoft SQ1 / SQ2 / SQ3 | Q24 | Microsoft SQ[123]\b
Qualcomm | Snapdragon X (X1, X1E, X1P) | Q24 | \bX1[EP]?-\d{2}-\d{3}\b|Snapdragon\(R\) X( Plus| Elite)?\b|Snapdragon X( Plus| Elite)?\b
";
}
