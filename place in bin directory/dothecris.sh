cp '/home/benedani/Games/Emulation/N64/ROMs/SM64 hacks/hacks/B3313/B3313_v1.0.2_hotfix_3.z64' 'b3313 silved.z64'
./BeeieOptimizer 'b3313 silved.z64' '/run/media/benedani/TrollUSB/Projects/Romhacking/beeiea3/beeiea3/TrollEngine/tools/Painting64/Painting64' '/run/media/benedani/TrollUSB/Projects/Romhacking/beeiea2n64/Bee/paintingcfg.txt' 
{ head -c 67108864 "b3313 silved.z64"; } > "b3313 chrised.z64"
