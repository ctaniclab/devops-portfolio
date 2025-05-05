# DevOps Portfolio Project

![CI/CD Status](https://img.shields.io/github/actions/workflow/status/ctaniclab/devops-portfolio/docker-build.yml)

Complete DevOps pipeline demonstrating:

- Infrastructure as Code (Terraform)
- CI/CD (GitHub Actions)
- Containerization (Docker)
- Orchestration (Kubernetes)
- Monitoring (Prometheus/Grafana)
- Logging (ELK Stack)

## Project Structure
devops-portfolio/
├── .github/workflows/ # CI/CD pipelines
├── infrastructure/ # IaC configurations
├── kubernetes/ # K8s manifests
├── scripts/ # Utility scripts
└── README.md # Project documentation


## Badges

[![Terraform](https://img.shields.io/badge/Terraform-7B42BC?style=for-the-badge&logo=terraform&logoColor=white)](https://www.terraform.io/)
[![Kubernetes](https://img.shields.io/badge/Kubernetes-326CE5?style=for-the-badge&logo=kubernetes&logoColor=white)](https://kubernetes.io/)
[![Docker](https://img.shields.io/badge/Docker-2496ED?style=for-the-badge&logo=docker&logoColor=white)](https://www.docker.com/)

## Quick Start

```bash
# Clone repository
git clone https://github.com/ctaniclab/devops-portfolio.git
cd devops-portfolio

# Initialize infrastructure
cd infrastructure/terraform
terraform init
terraform plan
terraform apply

# Deploy application
kubectl apply -f kubernetes/
