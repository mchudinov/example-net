# kubectl create namespace example-net
# helm install example-net --namespace example-net ./ 
# helm uninstall example-net --namespace example-net

# helm repo update

helm lint --strict
helm install --dry-run --debug --generate-name ./
helm package ./
mv example-net-helm* ./chart
helm repo index ./chart