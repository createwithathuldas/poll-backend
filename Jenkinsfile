pipeline {
    agent any

    environment {
        DOCKER_IMAGE = 'poll-api:latest'
        NETWORK_NAME = 'poll-network'
        MYSQL_CONTAINER = 'mysql-db'
        API_CONTAINER = 'poll-api-container'
    }

    stages {
        stage('checkout scm') {
            steps {
                checkout scm
            }
        }

        stage('checkout') {
            steps {
                echo 'Verifying workspace paths...'
                sh 'ls -la'
            }
        }

        stage('build docker image') {
            steps {
                script {
                    if (fileExists('PollApi')) {
                        dir('PollApi') {
                            sh "docker build -t ${DOCKER_IMAGE} ."
                        }
                    } else {
                        sh "docker build -t ${DOCKER_IMAGE} ."
                    }
                }
            }
        }

        stage('create network') {
            steps {
                script {
                    sh """
                        docker network inspect ${NETWORK_NAME} >/dev/null 2>&1 || \
                        docker network create ${NETWORK_NAME}
                    """
                }
            }
        }

        stage('start mysql') {
            steps {
                script {
                    // Notice: No -p 3306:3306 here. This prevents the port binding error.
                    sh """
                        docker rm -f ${MYSQL_CONTAINER} || true
                        docker run -d \
                            --name ${MYSQL_CONTAINER} \
                            --network ${NETWORK_NAME} \
                            -e MYSQL_ROOT_PASSWORD=root \
                            -e MYSQL_DATABASE=fifa_db \
                            mysql:8.0
                    """
                }
            }
        }

        stage('mysql health check') {
            steps {
                script {
                    echo 'Waiting for MySQL database to become healthy...'
                    sh """
                        timeout=60
                        while [ \$timeout -gt 0 ]; do
                            if docker exec ${MYSQL_CONTAINER} mysqladmin ping -uroot -proot --silent; then
                                echo 'MySQL is up and running!'
                                break
                            fi
                            echo 'Waiting for MySQL...'
                            sleep 2
                            timeout=\$((\$timeout - 2))
                        done
                        if [ \$timeout -le 0 ]; then
                            echo 'MySQL health check failed!'
                            exit 1
                        fi
                    """
                }
            }
        }

        stage('run Api') {
            steps {
                script {
                    sh "docker rm -f ${API_CONTAINER} || true"
                    sh """
                        docker run -d \
                            --name ${API_CONTAINER} \
                            --network ${NETWORK_NAME} \
                            -p 5298:5298 \
                            -e ConnectionStrings__Default="Server=${MYSQL_CONTAINER};Port=3306;Database=fifa_db;User=root;Password=root;" \
                            -e ASPNETCORE_ENVIRONMENT=Development \
                            ${DOCKER_IMAGE}
                    """
                }
            }
        }
    }
}