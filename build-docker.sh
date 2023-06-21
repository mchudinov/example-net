#!/bin/bash
docker login
docker build -t example-net .
docker tag example-net chudinov/example-net
docker push chudinov/example-net
