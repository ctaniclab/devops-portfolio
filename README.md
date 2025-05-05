# DevOps Portfolio Project

![CI/CD Pipeline](https://img.shields.io/github/actions/workflow/status/<ВАШ_USERNAME>/devops-portfolio/docker-build.yml)

Complete DevOps pipeline demonstrating:

- Infrastructure as Code (Terraform)
- CI/CD (GitHub Actions)
- Containerization (Docker)
- Orchestration (Kubernetes)
- Monitoring (Prometheus/Grafana)
- Logging (ELK Stack)

## Quick Start

```bash
# Clone repository
git clone https://github.com/<ВАШ_USERNAME>/devops-portfolio.git
cd devops-portfolio

# Initialize infrastructure
cd infrastructure/terraform
terraform init
terraform plan
terraform apply

# Deploy application
kubectl apply -f kubernetes/





cat > infrastructure/terraform/main.tf << 'EOF'
provider "aws" {
  region = "us-east-1"
}

resource "aws_vpc" "main" {
  cidr_block = "10.0.0.0/16"
  tags = {
    Name = "portfolio-vpc"
  }
}
