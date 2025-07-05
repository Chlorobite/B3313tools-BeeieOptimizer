cp '/run/media/benedani/TrollUSB/Projects/Romhacking/beeiea2n64/Bee/b3313 silved.z64' 'b3313 silved.z64'
cp '/run/media/benedani/TrollUSB/Projects/Romhacking/beeiea2n64/Bee/b3313 silved.config' 'b3313 silved.config'
./BeeieOptimizer 'b3313 silved.z64' '/run/media/benedani/TrollUSB/Projects/Romhacking/beeiea3/beeiea3/TrollEngine/tools/Painting64/Painting64' '/run/media/benedani/TrollUSB/Projects/Romhacking/beeiea2n64/Bee/paintingcfg.txt' 
{ head -c 50331648 "b3313 silved.z64"; } > "b3313 copped3.z64"
{ head -c 67108864 "b3313 silved.z64"; } > "b3313 copped4.z64"
