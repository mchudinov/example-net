#!/bin/bash
docker login
docker build -t example-net-docker .
docker tag example-net-docker ghcr.io/wss-ea-cloud-devops/example-net-docker:0.1.0
docker push ghcr.io/wss-ea-cloud-devops/example-net-docker:0.1.0
