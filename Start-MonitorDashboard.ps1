Write-Host "Starting Portainer (Monitor Dashboard)..." -ForegroundColor Cyan
# Create volume if not exists
docker volume create portainer_data > $null

# Run Portainer container
docker run -d -p 8000:8000 -p 9000:9000 --name portainer --restart=always -v //var/run/docker.sock:/var/run/docker.sock -v portainer_data:/data portainer/portainer-ce:latest

Write-Host "Portainer is starting..." -ForegroundColor Green
Write-Host "1. Open your browser and go to: http://localhost:9000" -ForegroundColor Yellow
Write-Host "2. Create a new admin password." -ForegroundColor Yellow
Write-Host "3. Select 'Get Started' (Local Docker environment)." -ForegroundColor Yellow
Write-Host "4. Go to 'Containers' or 'Stacks' to monitor CPU/RAM and manually scale!" -ForegroundColor Yellow