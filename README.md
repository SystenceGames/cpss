# cpss
Cluster PowerShell Session - a csshx inspired Powershell remoting tool

## Usage
Open a commandline to the binary and run
```
.\cpss.exe username=myUsername password=myPassword https://dothejig.com:5986/ http://dothejigagain.com:5985/
```
Notes: 
* username and password must be the same for all endpoints.  
* Any number of endpoints can be specified.  
* Parameters can come in any order.
