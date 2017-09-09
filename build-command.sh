#!/bin/bash

keys="$(grep -A2 'hudl_farm' ~/.aws/credentials | sed -n -e 's/aws_access_key_id=\([A-Z]*\)/--accesskey \1/p' -e 's/aws_secret_access_key=\([A-Z0-9a-z]*\)/ --secretkey \1/p' | sed '/\n$/!N;s/\n//')"

command="dotnet run"
case "$1" in 
find)
	command+=" find"
	;;
reboot)
	command+=" reboot"
	;;
*)
	echo $"Usage $0 {find|reboot} {stream|stream-med|stream-lrg|phoenix-reel-concat|phoenix-html-effects|phoenix-timeline-item|phoenix-privacy} {use1|apse2|euw1}"
	exit 1
esac
command+=" $keys "
if [[ $2 =~ stream-* ]]; then
	command+="--securityGroup prod-streamworker"
elif [ "$2" = "phoenix-reel-concat" ]; then
	command+="--securityGroup prod-phoenix-reel-concat"
elif [ "$2" = "phoenix-timeline-item" ]; then
	command+="--securityGroup prod-phoenix-timeline-item"
elif [ "$2" = "phoenix-html-effects" ]; then
	command+="--securityGroup prod-phoenix-html-effects"
elif [ "$2" = "phoenix-privacy" ]; then
	command+="--securityGroup prod-phoenix-privacy"
fi




case "$2" in
stream-lrg)
	command+="-lrg --name prod-farm-stream-lrg"
	;;
stream-med)
	command+="-med --name prod-farm-stream-med"
	;;
stream)
	command+=" --name prod-farm-stream"
	;;
phoenix-reel-concat)
	command+=" --name prod-farm-phoenix-reel-concat"
	;;
phoenix-html-effects)
	command+=" --name prod-farm-phoenix-html-effects"
	;;
phoenix-timeline-item)
	command+=" --name prod-farm-phoenix-timeline-item"
	;;
phoenix-privacy)
	command+=" --name prod-farm-phoenix-privacy"
	;;
*)
	echo $"Usage $0 {find|reboot} {stream|stream-med|stream-lrg|phoenix-reel-concat|phoenix-html-effects|phoenix-timeline-item|phoenix-privacy} {use1|apse2|euw1}"
	exit 1
esac

case "$3" in
apse2)
	command+="-apse2 --region ap-southeast-2"
	;;
euw1)
	command+="-euw1 --region eu-west-1"
	;;
use1)
	;;
*)
	echo $"Usage $0 {find|reboot} {stream|stream-med|stream-lrg|phoenix-reel-concat|phoenix-html-effects|phoenix-timeline-item|phoenix-privacy} {use1|apse2|euw1}"
	exit 1
esac
echo "$command"
exit 0
