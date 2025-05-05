<<<<<<< HEAD
# DevOps Portfolio Project

![CI/CD Pipeline](https://img.shields.io/github/actions/workflow/status/<ВАШ_USERNAME>/devops-portfolio/docker-build.yml)
=======
# devops-portfolio
Complete DevOps pipeline with CI/CD, Kubernetes, monitoring and logging
>>>>>>> a19b4a07fecaaad796b1dcce08c2094ffd76b5bb

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
<<<<<<< HEAD
git clone https://github.com/<ВАШ_USERNAME>/devops-portfolio.git
=======
git clone https://github.com/ctaniclab/devops-portfolio.git
>>>>>>> a19b4a07fecaaad796b1dcce08c2094ffd76b5bb
cd devops-portfolio

# Initialize infrastructure
cd infrastructure/terraform
terraform init
terraform plan
terraform apply

# Deploy application
kubectl apply -f kubernetes/
<<<<<<< HEAD





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
=======
>>>>>>> a19b4a07fecaaad796b1dcce08c2094ffd76b5bb
