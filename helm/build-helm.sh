# kubectl create namespace example-net-console
# helm install example-net-console --namespace example-net-console ./ 
# helm uninstall example-net-console --namespace example-net-console

# helm repo update

helm lint --strict
helm install --dry-run --debug --generate-name ./
helm package ./
mv example-net-console* ./chart
helm repo index ./chart