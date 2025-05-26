REM Cisco
gsshpass.exe -c "term len 0,show run,," -h 10.35.253.3:22 -p 4qKUQCG#Q!CiVLZtFS7J -u svc_netautomation -prompt "#" --prompt-count 3 --invoke-shell
REM Single command no shell: gsshpass.exe --c "show run" -h 172.16.1.101:22 -p cisco -u cisco
REM ION
gsshpass.exe --c "dump disk info,," -h 10.35.252.4:22 -p Th!$istheW@y -u rtradmin -prompt "#" --prompt-count 6 --invoke-shell
REM Palo
gsshpass.exe --c "set cli pager off,show system info,," -h 10.35.191.251:22 -p Th!$istheW@y -u admin -prompt ")>" --prompt-count 3 --invoke-shell